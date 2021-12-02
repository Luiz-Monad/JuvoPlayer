/*!
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
using System.Diagnostics;

namespace JuvoLogger
{
    public class Logger : ILogger
    {
        private const string Tag = "JuvoPlayer";
        private readonly TraceSource logger;

        public Logger(string channel)
        {
            logger = new TraceSource($"{Tag}.{channel}");
        }

        private const string format = "{1}: {2}:{3} > {0}";

        public void Verbose(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Verbose, 0, format, message, file, method, line);
        }

        public void Debug(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Verbose, 1, format, message, file, method, line);
        }

        public void Info(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Information, 0, format, message, file, method, line);
        }

        public void Warn(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Warning, 0, format, message, file, method, line);
        }

        public void Warn(Exception ex, string message, string file = "", string method = "", int line = 0)
        {
            if (!string.IsNullOrEmpty(message))
                logger.TraceEvent(TraceEventType.Warning, 0, format, message, file, method, line);
            logger.TraceEvent(TraceEventType.Warning, 0, format, ex.Message, file, method, line);
        }

        public void Error(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Error, 0, format, message, file, method, line);
        }

        public void Error(Exception ex, string message, string file = "", string method = "", int line = 0)
        {
            if (!string.IsNullOrEmpty(message))
                logger.TraceEvent(TraceEventType.Error, 0, format, message, file, method, line);
            logger.TraceEvent(TraceEventType.Error, 0, format, ex.Message, file, method, line);
            logger.TraceEvent(TraceEventType.Error, 0, format, ex.Source, file, method, line);
            logger.TraceEvent(TraceEventType.Error, 0, format, ex.StackTrace, file, method, line);
        }

        public void Fatal(string message, string file = "", string method = "", int line = 0)
        {
            logger.TraceEvent(TraceEventType.Critical, 0, format, message, file, method, line);
        }

        public void Fatal(Exception ex, string message, string file = "", string method = "", int line = 0)
        {
            if (!string.IsNullOrEmpty(message))
                logger.TraceEvent(TraceEventType.Critical, 0, format, message, file, method, line);
            logger.TraceEvent(TraceEventType.Critical, 0, format, ex.Message, file, method, line);
            logger.TraceEvent(TraceEventType.Critical, 0, format, ex.Source, file, method, line);
            logger.TraceEvent(TraceEventType.Critical, 0, format, ex.StackTrace, file, method, line);
        }

    }
}
