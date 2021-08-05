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
using System.Runtime.InteropServices;
using FFmpegBindings.Interop;
using JuvoLogger;
using ffmpeg = FFmpegBindings.Interop.FFmpeg;

namespace JuvoPlayer.Demuxers.FFmpeg
{
    public class FFmpegGlue : IFFmpegGlue
    {
        public void Initialize()
        {
            try
            {
                ffmpeg.av_register_all();
                ffmpeg.avformat_network_init();
                unsafe
                {
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
                    av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
                    {
                        if (level > ffmpeg.av_log_get_level())
                            return;

                        const int lineSize = 1024;
                        var lineBuffer = stackalloc byte[lineSize];
                        var printPrefix = 1;
                        ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                        var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);

                        Log.Warn(line);
                    };
                    ffmpeg.av_log_set_callback(logCallback);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not load and register FFmpeg library");
                throw new DemuxerException("Could not load and register FFmpeg library", e);
            }
        }

        public IAvioContext AllocIoContext(ulong bufferSize, ReadPacket readPacket, SeekFunction seekFunction)
        {
            return new AvioContextWrapper(bufferSize, readPacket, seekFunction);
        }

        public IAvFormatContext AllocFormatContext()
        {
            return new AvFormatContextWrapper();
        }
    }
}
