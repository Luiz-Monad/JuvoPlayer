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
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using JuvoLogger;

namespace JuvoPlayer.Tests.Utils
{
    public class RunningClockTask
    {
        private readonly ILogger _logger = LoggerManager.GetInstance().GetLogger("UT");
        private readonly IPlayerService _service;
        private const int ConsecutiveClocks = 3;
        private readonly CancellationToken _cancellationToken;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _initialClock;
        private readonly SynchronizationContext _syncCtx;

        public RunningClockTask(IPlayerService service, CancellationToken token, TimeSpan timeout, TimeSpan initialClock)
        {
            _service = service;
            _cancellationToken = token;
            _timeout = timeout;
            _initialClock = initialClock;

            // Grab synchronization context as it's not inherited to Task.Run()
            if (SynchronizationContext.Current == null)
                throw new ArgumentNullException(nameof(SynchronizationContext.Current), "Synchronization context cannot be null");

            _syncCtx = SynchronizationContext.Current;
        }

        public Task Observe()
        {
            return Task.Run(async () =>
            {
                var observedClocks = new List<(DateTimeOffset timeStamp, TimeSpan clock)>();

                try
                {
                    var observedCount = 0;
                    var lastObservedClock = _initialClock;

                    await _service.PlayerClock()
                        .ObserveOn(_syncCtx)
                        .FirstAsync(clk =>
                        {
                            observedClocks.Add((DateTimeOffset.Now, clk));

                            if (clk <= lastObservedClock)
                                return false;

                            lastObservedClock = clk;
                            observedCount++;

                            return observedCount >= ConsecutiveClocks;
                        })
                        .Timeout(_timeout)
                        .ToTask(_cancellationToken);

                }
                catch (Exception)
                {
                    _logger.Error($"Running clock error. Timeout {_timeout} Expected: {_initialClock}");
                    foreach (var clockEntry in observedClocks)
                    {
                        _logger.Error($"{clockEntry.timeStamp} {clockEntry.clock}");
                    }

                    throw;
                }
            }, _cancellationToken);
        }

        public static Task Observe(IPlayerService service, TimeSpan initialClock, CancellationToken token, TimeSpan timeout)
        {
            return new RunningClockTask(service, token, timeout, initialClock).Observe();
        }

        public static Task Observe(IPlayerService service, CancellationToken token, TimeSpan timeout)
        {
            return new RunningClockTask(service, token, timeout, TimeSpan.Zero).Observe();
        }
    }
}
