﻿/*!
 * https://github.com/SamsungDForum/JuvoPlayer
 * Copyright 2020, Samsung Electronics Co., Ltd
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
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using JuvoLogger;
using JuvoPlayer.Common;
using JuvoPlayer.Drms;

namespace JuvoPlayer.Players
{
    public class StreamRenderer : IStreamRenderer
    {
        private readonly PacketSynchronizer _packetSynchronizer;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly CdmContext _cdmContext;
        private readonly BufferingObserver _bufferingObserver;

        public StreamRenderer(
            PacketSynchronizer packetSynchronizer,
            CdmContext cdmContext,
            BufferingObserver bufferingObserver)
        {
            _packetSynchronizer = packetSynchronizer;
            _cdmContext = cdmContext;
            _bufferingObserver = bufferingObserver;
        }

        public bool IsPushingPackets { get; private set; }

        public void HandlePacket(Packet packet)
        {
            Log.Info($"{packet.StreamType} {packet.Pts}");
            _packetSynchronizer.Add(packet);
        }

        public void HandleDrmInitData(DrmInitData drmInitData)
        {
            _cdmContext.AddDrmInitData(drmInitData);
        }

        public async Task StartPushingPackets(
            Segment segment,
            IPlatformPlayer platformPlayer)
        {
            try
            {
                if (IsPushingPackets)
                    return;
                IsPushingPackets = true;

                _packetSynchronizer.Segment = segment;

                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                while (!cancellationToken.IsCancellationRequested)
                {
                    using (var packet = await _packetSynchronizer.TakeAsync(cancellationToken))
                    {
                        var result = await SubmitPacket(
                            platformPlayer,
                            packet,
                            cancellationToken);
                        Log.Info($"{packet.StreamType} {packet.Pts} {result}");
                        if (result != SubmitResult.Success)
                        {
                            throw new NotImplementedException(
                                "TODO: Handle other results");
                        }

                        _bufferingObserver.Update(packet);
                    }

                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private async Task<SubmitResult> SubmitPacket(
            IPlatformPlayer platformPlayer,
            Packet packet,
            CancellationToken cancellationToken)
        {
            if (!(packet is EncryptedPacket encryptedPacket))
                return platformPlayer.SubmitPacket(packet);
            using (var decryptedPacket = await DecryptPacket(
                encryptedPacket,
                cancellationToken))
            {
                return platformPlayer.SubmitPacket(decryptedPacket);
            }
        }

        public void StopPushingPackets()
        {
            Log.Info();
            _cancellationTokenSource?.Cancel();
            IsPushingPackets = false;
        }

        public void Flush()
        {
            Log.Info();
            _packetSynchronizer.Flush();
        }

        private async Task<Packet> DecryptPacket(
            EncryptedPacket encryptedPacket,
            CancellationToken cancellationToken)
        {
            var cdmInstance = _cdmContext.GetCdmInstance();
            do
            {
                try
                {
                    return cdmInstance.Decrypt(encryptedPacket);
                }
                catch (NoKeyException)
                {
                    Log.Warn("Waiting for key");
                    await cdmInstance
                        .OnKeyStatusChanged()
                        .FirstAsync()
                        .ToTask(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            } while (true);
        }
    }
}