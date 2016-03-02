using System;
using System.IO;
using System.IO.Pipes;
using System.IO.Ports;
using System.Text;
using System.Threading;
using log4net;
using System.Threading.Tasks;

namespace NP2COMV
{
    using System.ComponentModel;

    public class Connection
    {
        private SerialPort serialPort;
        private NamedPipeClientStream namedPipe;
        private Thread portForwarder;
        private AutoResetEvent stopEvent;
        public delegate void ProcessStatus(bool status);
        private int comReadTimeout = 5;
        private int pipeReadTimeout = 5;
        private int waitTimeout = 2000;

        #region Logging

        private static readonly ILog Logger = LogManager.GetLogger(typeof(Connection));

        private static string GetLogString(byte[] buffer, int length)
        {
            return Encoding.UTF8.GetString(buffer, 0, length).Replace("\r", "\\r").Replace("\n", "\\n");
        }

        #endregion

        #region Public functions and .ctor

        public Connection(Settings settings,ISynchronizeInvoke context, ProcessStatus update)
        {
            this.stopEvent = new AutoResetEvent(false);

            CurrentSettings = settings;
            Context = context;
            Update = update;

            IsStarted = false;
        }
        
        public void Start()
        {
            Logger.Info("Starting proxy: " + CurrentSettings.ComPort + " -> " + CurrentSettings.NamedPipe);

            this.serialPort = 
                new SerialPort(
                    CurrentSettings.ComPort, 
                    CurrentSettings.BaudRate, 
                    CurrentSettings.Parity, 
                    CurrentSettings.DataBits,
                    CurrentSettings.StopBits)
                    {
                        RtsEnable = true,
                        DtrEnable = true,
                        Encoding = Encoding.UTF8
                     };

            this.namedPipe = 
                new NamedPipeClientStream(
                    CurrentSettings.MachineName, 
                    CurrentSettings.NamedPipe, 
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

            this.portForwarder = new Thread(this.PortForwarder);          
            this.portForwarder.Start();
            this.IsStarted = true;

            SetStatus(true);
            Logger.Debug("Thread started");
        }

        private void ClosePorts()
        {
            /* clean up your fucking mess, you slob */
            if (this.serialPort.IsOpen)
                this.serialPort.Close();
            Logger.Debug("Serial port closed");
            if (this.namedPipe.IsConnected)
                this.namedPipe.Close();
            Logger.Debug("Named pipe closed");
        }

        public void Stop()
        {
            Logger.Info("Stopping proxy: " + CurrentSettings.ComPort + " -> " + CurrentSettings.NamedPipe);

            Thread closePorts = new Thread(ClosePorts);
            closePorts.Start();

            // Signal the port forwarder thread to stop
            this.stopEvent.Set();
            // Wait for port forwarder thread to stop

            this.portForwarder.Join(5000);
            this.IsStarted = false;

            SetStatus(false);
            Logger.Debug("Thread stopped");
        }

        public void SetStatus(bool status)
        {
            object[] param = new object[1];
            param[0] = status;
            Context.Invoke(Update, param);
        }

        #endregion

        #region Static Thread functions

        private void PortForwarder()
        {
            Boolean errorsOccurred = false;
            int namedPipeTimeout = 5000;

            try
            {
                this.serialPort.Open();
            }
            catch(IOException e)
            {
                errorsOccurred = true;
                Logger.Error("Error opening COM port");
            }
            try
            {
                this.namedPipe.Connect(namedPipeTimeout);
                this.namedPipe.ReadMode = PipeTransmissionMode.Byte;
            }
            catch (UnauthorizedAccessException e)
            {
                errorsOccurred = true;
                Logger.Error("Ensure that COM port redirector is running with administrator credentials");
            }
            catch (IOException e)
            {
                errorsOccurred = true;
                Logger.Error("Named pipe semaphore timeout");
            }
            catch (TimeoutException e)
            {
                errorsOccurred = true;
                Logger.Error("Timed out connecting to named pipe");
            }

            if(!this.serialPort.IsOpen || !this.namedPipe.IsConnected)
            {
                errorsOccurred = true;
                Logger.Error("Ensure that serial device is connected and that VM is running before starting service");
            }

            /* if no errors occurred whilst opening serial port and connecting to named pipe, let's do this shit */
            if (!errorsOccurred)
            {
                this.SerialProxy();
                //this.AsyncSerialProxy();
            }
            else
            {
                this.Stop();
            }
        }

        private void SerialProxy()
        {
            byte[] serialBuffer = new byte[this.serialPort.ReadBufferSize];
            byte[] pipeBuffer = new byte[this.serialPort.ReadBufferSize];

            ManualResetEvent pipeEvent = new ManualResetEvent(true);
            ManualResetEvent serialEvent = new ManualResetEvent(true);

            int waitResult;

            do
            {
                /* if the serial port unexpectedly closes.. don't shit the bed */
                if (!this.serialPort.IsOpen || !this.namedPipe.IsConnected)
                {
                    this.Stop();
                    break;
                }

                /* read from named pipe and write to serial port */
                if (pipeEvent.WaitOne(pipeReadTimeout))
                {
                    NRead(pipeEvent);
                }

                /* if the serial port unexpectedly closes.. don't shit the bed */
                if (!this.serialPort.IsOpen || !this.namedPipe.IsConnected)
                {
                    this.Stop();
                    break;
                }

                /* read from serial port and write to named pipe */
                if (serialEvent.WaitOne(comReadTimeout))
                {
                    CRead(serialEvent);

                }

                WaitHandle[] waitHandle = new WaitHandle[]
                {
                    serialEvent,
                    pipeEvent,
                    stopEvent
                };

                waitResult = WaitHandle.WaitAny(waitHandle, waitTimeout);
            }
            while (waitResult != 2);

        }

        /* NOT YET IMPLEMENTED
        private async void AsyncSerialProxy()
        {
            byte[] serialBuffer = new byte[this.serialPort.ReadBufferSize];
            byte[] pipeBuffer = new byte[this.serialPort.ReadBufferSize];

            do
            {
                if (!this.serialPort.IsOpen || !this.namedPipe.IsConnected)
                {
                    this.Stop();
                    break;
                }

                await this.serialPort.BaseStream.ReadAsync(serialBuffer, 0, serialBuffer.Length);
                await this.namedPipe.WriteAsync(serialBuffer, 0, serialBuffer.Length);
                Logger.Debug("COM-->NP: " + GetLogString(serialBuffer, serialBuffer.Length));

                await this.namedPipe.ReadAsync(pipeBuffer, 0, pipeBuffer.Length);
                await this.serialPort.BaseStream.WriteAsync(pipeBuffer, 0, pipeBuffer.Length);
                Logger.Debug("NP-->COM: " + GetLogString(pipeBuffer, pipeBuffer.Length));

            }
            while (true);
        }
        */

        private void CRead(ManualResetEvent serialEvent)
        {
            serialEvent.Reset();
            byte[] serialBuffer = new byte[this.serialPort.ReadBufferSize];

            try
            {
                this.serialPort.BaseStream.BeginRead(
                    serialBuffer,
                    0,
                    serialBuffer.Length,
                    NPWrite(serialEvent, serialBuffer),
                    null);
            }
            catch (InvalidOperationException)
            {

            }
        }

        private void NRead(ManualResetEvent pipeEvent)
        {
            pipeEvent.Reset();
            byte[] pipeBuffer = new byte[this.serialPort.ReadBufferSize];

            try
            {
                this.namedPipe.BeginRead(
                    pipeBuffer,
                    0,
                    pipeBuffer.Length,
                    CPWrite(pipeEvent, pipeBuffer),
                    null);
            }
            catch (InvalidOperationException)
            {

            }
        }

        private AsyncCallback NPWrite(ManualResetEvent serialEvent, byte[] serialBuffer)
        {
            return delegate (IAsyncResult AsyncResult)
            {
                try
                {
                    int actualLength = this.serialPort.BaseStream.EndRead(AsyncResult);
                    this.namedPipe.BeginWrite(
                        serialBuffer,
                        0,
                        actualLength,
                        NPEndWrite(serialBuffer,actualLength)
                        , null);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                    // Aborted due to close
                }
                catch (InvalidOperationException)
                {
                    // Aborted due to close
                }
                serialEvent.Set();
            };
        }

        private AsyncCallback CPWrite(ManualResetEvent pipeEvent, byte[] pipeBuffer)
        {
            return delegate (IAsyncResult AsyncResult)
            {
                try
                {
                    int actualLength = this.namedPipe.EndRead(AsyncResult);
                    this.serialPort.BaseStream.BeginWrite(
                        pipeBuffer,
                        0,
                        actualLength,
                        CPEndWrite(pipeBuffer, actualLength)
                        , null);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                    // Aborted due to close
                }
                catch (InvalidOperationException)
                {
                    // Aborted due to close
                }
                pipeEvent.Set();
            };
        }

        private AsyncCallback CPEndWrite(byte[] pipeBuffer, int length)
        {
            return delegate (IAsyncResult AsyncResult)
            {
                try
                {
                    this.serialPort.BaseStream.EndWrite(AsyncResult);
                    //Logger.Debug("COM<--NP: " + GetLogString(pipeBuffer, length));
                }
                catch (IOException)
                {
                    Logger.Debug("Error writing to COM port");
                }

            };
        }

        private AsyncCallback NPEndWrite(byte[] serialBuffer, int length)
        {
            return delegate (IAsyncResult AsyncResult)
            {
                try
                {
                    this.namedPipe.EndWrite(AsyncResult);
                    //Logger.Debug("COM-->NP: " + GetLogString(serialBuffer, length));
                }
                catch (IOException)
                {
                    Logger.Debug("Error writing to named pipe");
                }

            };
        }

        #endregion

        #region Properties

        public bool IsStarted { get; private set; }
        protected Settings CurrentSettings { get; private set; }
        private static ISynchronizeInvoke Context;
        private static ProcessStatus Update;
        #endregion
    }
}