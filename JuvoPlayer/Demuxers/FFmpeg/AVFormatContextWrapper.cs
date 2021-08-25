﻿/*!
 * https://github.com/SamsungDForum/JuvoPlayer
 * Copyright 2018, Samsung Electronics Co., Ltd
 * Licensed under the MIT license
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FFmpegBindings.Interop;
using JuvoLogger;
using JuvoPlayer.Common;
using ffmpeg = FFmpegBindings.Interop.FFmpeg;

namespace JuvoPlayer.Demuxers.FFmpeg
{
    public unsafe class AvFormatContextWrapper : IAvFormatContext
    {
        private const int MillisecondsPerSecond = 1000;

        private readonly AVRational _millsBase = new AVRational { num = 1, den = MillisecondsPerSecond };

        private readonly Dictionary<int, byte[]> _parsedExtraDatas = new Dictionary<int, byte[]>();
        private AvioContextWrapper _avioContext;

        private AVFormatContext* _formatContext;
        private bool _disposed;

        public AvFormatContextWrapper()
        {
            _formatContext = ffmpeg.avformat_alloc_context();
            if (_formatContext == null)
                throw new FFmpegException("Cannot allocate AVFormatContext");
        }

        public long ProbeSize
        {
            get => _formatContext->probesize;
            set => _formatContext->probesize = value;
        }

        public TimeSpan MaxAnalyzeDuration
        {
            get => TimeSpan.FromMilliseconds(_formatContext->max_analyze_duration);
            set => _formatContext->max_analyze_duration = (long) value.TotalMilliseconds;
        }

        public IAvioContext AvioContext
        {
            get => _avioContext;
            set
            {
                if (value == null)
                {
                    _formatContext->pb = null;
                }
                else
                {
                    if (value.GetType() != typeof(AvioContextWrapper))
                        throw new FFmpegException($"Unexpected context type. Got {value.GetType()}");
                    _avioContext = (AvioContextWrapper) value;
                    _formatContext->pb = _avioContext.Context;
                }
            }
        }

        public TimeSpan Duration => _formatContext->duration > 0
            ? TimeSpan.FromMilliseconds(_formatContext->duration / 1000)
            : TimeSpan.Zero;

        public DrmInitData DrmInitData => GetDrmInitData();
        public uint NumberOfStreams => _formatContext->nb_streams;

        public void Open()
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            _formatContext->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO | ffmpeg.AVFMT_FLAG_IGNIDX;
            Open(null);
        }

        public void Open(string url)
        {
            fixed
                (AVFormatContext** formatContextPointer = &_formatContext)
            {
                var ret = ffmpeg.avformat_open_input(formatContextPointer, url, null, null);
                if (ret != 0)
                    throw new FFmpegException("Cannot open AVFormatContext");
            }
        }

        public void FindStreamInfo()
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            var ret = ffmpeg.avformat_find_stream_info(_formatContext, null);
            if (ret < 0)
                throw new FFmpegException($"Could not find stream info (error code: {ret})!");
        }

        public int FindBestStream(AVMediaType mediaType)
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            return ffmpeg.av_find_best_stream(_formatContext, mediaType, -1, -1, null, 0);
        }

        public int FindBestBandwidthStream(AVMediaType mediaType)
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            ulong bandwidth = 0;
            var streamId = -1;
            for (var i = 0; i < _formatContext->nb_streams; ++i)
            {
                if (_formatContext->streams[i]->codecpar->codec_type != mediaType)
                    continue;
                var dict = ffmpeg.av_dict_get(_formatContext->streams[i]->metadata, "variant_bitrate", null, 0);
                if (dict == null)
                    return -1;
                var stringValue = Marshal.PtrToStringAnsi((IntPtr) dict->value);
                if (!ulong.TryParse(stringValue, out var value))
                {
                    Log.Error($"Expected to received an ulong, but got {stringValue}");
                    continue;
                }

                if (bandwidth >= value)
                    continue;
                streamId = i;
                bandwidth = value;
            }

            return streamId;
        }

        public void EnableStreams(IEnumerable<int> indexes)
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            for (var i = 0; i < _formatContext->nb_streams; i++)
                _formatContext->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
            foreach (var index in indexes)
            {
                if (index >= 0 && index < _formatContext->nb_streams)
                {
                    Log.Info($"Enabling {index}");
                    _formatContext->streams[index]->discard = AVDiscard.AVDISCARD_DEFAULT;
                }
            }
        }

        public StreamConfig ReadConfig(int idx)
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");
            VerifyStreamIndex(idx);

            var stream = _formatContext->streams[idx];
            switch (stream->codec->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    return ReadAudioConfig(stream);
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    return ReadVideoConfig(stream);
                default:
                    throw new FFmpegException($"Unsupported stream type: {stream->codec->codec_type}");
            }
        }

        public Packet NextPacket()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AvFormatContextWrapper));
            if (_formatContext == null)
                return null;

            do
            {
                var pkt = default(AVPacket);
                ffmpeg.av_init_packet(&pkt);
                pkt.data = null;
                pkt.size = 0;
                var ret = ffmpeg.av_read_frame(_formatContext, &pkt);
                if (ret == -541478725)
                    return null;
                if (ret < 0)
                    throw new FFmpegException($"Cannot get next packet. Cause {GetErrorText(ret)}");

                var streamIndex = pkt.stream_index;
                var stream = _formatContext->streams[streamIndex];
                var pts = Rescale(pkt.pts, stream);
                var dts = Rescale(pkt.dts, stream);

                int size;
                var sideData = ffmpeg.av_packet_get_side_data(&pkt,
                    AVPacketSideDataType.AV_PKT_DATA_ENCRYPTION_INFO, &size);
                var packet = sideData != null ? CreateEncryptedPacket(sideData, (ulong) size) : new Packet();

                packet.StreamType = stream->codec->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO
                    ? StreamType.Audio
                    : StreamType.Video;
                packet.Pts = pts.Ticks >= 0 ? pts : TimeSpan.Zero;
                packet.Dts = dts.Ticks >= 0 ? dts : TimeSpan.Zero;
                packet.Duration = Rescale(pkt.duration, stream);

                packet.IsKeyFrame = (pkt.flags & 1) != 0;
                packet.Storage = new FFmpegDataStorage { Packet = pkt, StreamType = packet.StreamType };
                PrependExtraDataIfNeeded(packet, stream);
                return packet;
            } while (true);
        }

        public void Seek(TimeSpan time)
        {
            if (_formatContext == null)
                throw new FFmpegException($"Format context is null");

            var idx = -1;

            for (var i = 0; i < _formatContext->nb_streams; i++)
            {
                var stream = _formatContext->streams[i];
                if (stream->discard != AVDiscard.AVDISCARD_DEFAULT)
                    continue;

                if (stream->codec->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    idx = i;
                    break;
                }

                if (stream->codec->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    idx = i;
            }

            VerifyStreamIndex(idx);
            HandleSeek(idx, time);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
            _disposed = true;
        }

        private static int SwapEndianess(int value)
        {
            var t = 0xff;
            var b1 = (value >> 0) & t;
            var b2 = (value >> 8) & t;
            var b3 = (value >> 16) & t;
            var b4 = (value >> 24) & t;

            return b1 << 24 | b2 << 16 | b3 << 8 | b4 << 0;
        }

        private byte[] BuildPsshAtom(AVEncryptionInitInfo* e)
        {
            int psshBoxLength = 8 /*HEADER*/ + 16 /*SysID*/ + 4 /*DataSize*/ + (int) e->data_size + 4 /*version*/;
            if (e->key_ids != null && e->num_key_ids != 0)
            {
                psshBoxLength += 4 /*KID_count*/ + ((int) e->num_key_ids * 16);
            }

            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(SwapEndianess(psshBoxLength));

                foreach (var c in "pssh") writer.Write(c);

                writer.Write(e->key_ids != null && e->num_key_ids != 0 ? SwapEndianess(0x01000000) : 0x00000000);

                for (int i = 0; i < 16; i++)
                {
                    writer.Write(e->system_id[i]);
                }

                if (e->key_ids != null && e->num_key_ids != 0)
                {
                    writer.Write(SwapEndianess((int) e->num_key_ids));
                    for (int i = 0; i < e->num_key_ids; i++)
                    {
                        for (int j = 0; j < 16; j++)
                        {
                            writer.Write(e->key_ids[i][j]);
                        }
                    }
                }

                if (e->data != null && e->data_size != 0)
                {
                    writer.Write(SwapEndianess((int) e->data_size));
                    for (int i = 0; i < e->data_size; i++)
                    {
                        writer.Write(e->data[i]);
                    }
                }
                else
                {
                    writer.Write(0);
                }

                return stream.ToArray();
            }
        }

        private DrmInitData GetDrmInitData()
        {
            var f = _formatContext;
            var result = new List<byte>();

            for (var i = 0; i < f->nb_streams; i++)
            {
                int size;
                var enc = ffmpeg.av_stream_get_side_data(
                    f->streams[i],
                    AVPacketSideDataType.AV_PKT_DATA_ENCRYPTION_INIT_INFO,
                    &size);
                if (enc == null)
                    continue;

                var data = ffmpeg.av_encryption_init_info_get_side_data(enc, (ulong) size);
                while (data != null)
                {
                    var psshBox = BuildPsshAtom(data).ToList();
                    result.AddRange(psshBox);
                    data = data->next;
                }
            }

            if (result.Count == 0)
                return null;
            return new DrmInitData
            {
                DataType = DrmInitDataType.Cenc,
                Data = result.ToArray()
            };
        }

        private void PrependExtraDataIfNeeded(Packet packet, AVStream* stream)
        {
            if (!packet.IsKeyFrame || packet.StreamType != StreamType.Video)
                return;

            if (!_parsedExtraDatas.ContainsKey(stream->index))
            {
                var codec = stream->codec;
                _parsedExtraDatas[stream->index] = CodecExtraDataParser.Parse(codec->codec_id, codec->extradata,
                    codec->extradata_size);
            }

            var parsedExtraData = _parsedExtraDatas[stream->index];
            if (parsedExtraData != null)
                packet.Prepend(parsedExtraData);
        }

        private TimeSpan Rescale(long ffmpegTime, AVStream* stream)
        {
            var rescaled = ffmpeg.av_rescale_q(ffmpegTime, stream->time_base, _millsBase);
            return TimeSpan.FromMilliseconds(rescaled);
        }

        private void VerifyStreamIndex(int idx)
        {
            if (idx < 0 || idx >= _formatContext->nb_streams)
            {
                throw new FFmpegException(
                    $"Wrong stream index! nb_streams = {_formatContext->nb_streams}, idx = {idx}");
            }
        }

        private void HandleSeek(int idx, TimeSpan time)
        {
            var target = CalculateTarget(idx, time);
            var ret = ffmpeg.av_seek_frame(_formatContext, idx, target, ffmpeg.AVSEEK_FLAG_BACKWARD);

            if (ret != 0)
                throw new FFmpegException($"av_seek_frame returned {ret}");
        }

        private long CalculateTarget(int idx, TimeSpan time)
        {
            var stream = _formatContext->streams[idx];
            var target =
                ffmpeg.av_rescale_q((long) time.TotalMilliseconds, _millsBase, stream->time_base);

            if (target < stream->first_dts)
                return stream->first_dts;

            if (stream->index_entries != null && stream->nb_index_entries > 0)
            {
                var lastTimestamp = stream->index_entries[stream->nb_index_entries - 1].timestamp;
                if (target > lastTimestamp)
                    return lastTimestamp;
            }

            if (stream->duration > 0 && target > stream->duration)
                return stream->duration;

            return target;
        }

        private static Packet CreateEncryptedPacket(byte* sideData, ulong size)
        {
            var encInfo = ffmpeg.av_encryption_info_get_side_data(sideData, size);
            int subsampleCount = (int) encInfo->subsample_count;

            byte[] arrkid = new byte[encInfo->key_id_size];
            Marshal.Copy((IntPtr) encInfo->key_id, arrkid, 0, (int) encInfo->key_id_size);

            byte[] arriv = new byte[encInfo->iv_size];
            Marshal.Copy((IntPtr) encInfo->iv, arriv, 0, (int) encInfo->iv_size);
            var packet = new EncryptedPacket
            {
                KeyId = arrkid,
                Iv = arriv
            };
            if (subsampleCount <= 0)
                return packet;
            packet.SubSamples = new EncryptedPacket.Subsample[subsampleCount];
            // structure has sequential layout and the last element is an array
            // due to marshalling error we need to define this as single element
            // so to read this as an array we need to get a pointer to first element
            var subsamples = encInfo->subsamples;
            for (var i = 0; i < subsampleCount; ++i)
            {
                packet.SubSamples[i].ClearData = subsamples[i].bytes_of_clear_data;
                packet.SubSamples[i].EncData = subsamples[i].bytes_of_protected_data;
            }

            return packet;
        }

        private static StreamConfig ReadAudioConfig(AVStream* stream)
        {
            var codecExtraData = GetCodecExtraData(stream);
            var mimeType = GetMimeType(stream);
            var channelLayout = stream->codecpar->channels;
            var sampleRate = stream->codecpar->sample_rate;
            var bitsPerChannel = GetBitsPerChannel(stream);
            var bitRate = stream->codecpar->bit_rate;
            var language = GetLanguage(stream);
            var index = stream->index;
            return new FFmpegAudioStreamConfig(
                codecExtraData,
                mimeType,
                channelLayout,
                sampleRate,
                bitsPerChannel,
                bitRate,
                language,
                index);
        }

        private static StreamConfig ReadVideoConfig(AVStream* stream)
        {
            var codecExtraData = GetCodecExtraData(stream);
            var mimeType = GetMimeType(stream);
            var size = new Size(
                stream->codecpar->width,
                stream->codecpar->height);
            var frameRateNum = stream->avg_frame_rate.num;
            var frameRateDen = stream->avg_frame_rate.den > 0 ? stream->avg_frame_rate.den : 1;
            var bitRate = stream->codecpar->bit_rate;
            var index = stream->index;
            return new FFmpegVideoStreamConfig(
                codecExtraData,
                mimeType,
                size,
                frameRateNum,
                frameRateDen,
                bitRate,
                index);
        }

        private static byte[] GetCodecExtraData(AVStream* stream)
        {
            if (stream->codecpar->extradata_size <= 0)
                return null;
            var codecExtraData = new byte[stream->codecpar->extradata_size];
            Marshal.Copy((IntPtr) stream->codecpar->extradata, codecExtraData, 0,
                stream->codecpar->extradata_size);
            return codecExtraData;
        }

        private static int GetBitsPerChannel(AVStream* stream)
        {
            int bitsPerChannel;
            if (stream->codecpar->bits_per_coded_sample > 0)
            {
                bitsPerChannel = stream->codecpar->bits_per_coded_sample;
            }
            else
            {
                var sampleFormat = (AVSampleFormat) stream->codecpar->format;
                bitsPerChannel = ffmpeg.av_get_bytes_per_sample(sampleFormat) * 8;
                bitsPerChannel /= stream->codecpar->channels;
            }

            return bitsPerChannel;
        }

        private static string GetLanguage(AVStream* stream)
        {
            var languageEntry = ffmpeg.av_dict_get(stream->metadata, "language", null, 0);
            return languageEntry != null ?
                Marshal.PtrToStringAnsi((IntPtr) languageEntry->value, 3) :
                null;
        }

        private static string GetMimeType(AVStream* stream)
        {
            var codec = stream->codecpar->codec_id;
            return codec.ConvertToMimeType();
        }

        private static string GetErrorText(int returnCode)
        {
            const int errorBufferSize = 1024;
            var errorBuffer = new byte[errorBufferSize];
            try
            {
                fixed (byte* errbuf = errorBuffer)
                {
                    ffmpeg.av_strerror(returnCode, errbuf, errorBufferSize);
                }
            }
            catch (Exception)
            {
                return "";
            }

            return Encoding.UTF8.GetString(errorBuffer);
        }

        private void ReleaseUnmanagedResources()
        {
            if (_formatContext == null)
                return;
            fixed (AVFormatContext** formatContextPointer = &_formatContext)
            {
                ffmpeg.avformat_close_input(formatContextPointer);
            }
        }

        ~AvFormatContextWrapper()
        {
            ReleaseUnmanagedResources();
        }
    }
}