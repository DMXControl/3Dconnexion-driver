﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using _3Dconnexion;
using System.Threading;
using System.Runtime.InteropServices;

namespace _3DconnexionDriver
{
    public class _3DconnexionDevice : IDisposable
    {
        IntPtr _deviceHandle = IntPtr.Zero;
        int _leds = -1;

        public event EventHandler ZeroPoint;
        public event EventHandler<MotionEventArgs> Motion;
        public event EventHandler<DeviceChangeEventArgs> DeviceChange;

        private readonly System.Threading.Thread eventThread;
        private readonly Dictionary<SiApp.SiEventType, EventArgs> eventBuffer = new Dictionary<SiApp.SiEventType, EventArgs>();

        public _3DconnexionDevice(string appName)
        {
            this.AppName = appName;
            eventThread = new System.Threading.Thread(EventThreadLoop);
            eventThread.IsBackground = true;
            eventThread.Start();
        }

        ~_3DconnexionDevice()
        {
            Dispose();
        }

        public bool IsAvailable
        {
            get { return _deviceHandle != IntPtr.Zero; }
        }

        public bool IsDisposed
        {
            get;
            private set;
        }

        public string AppName
        {
            get; 
            private set;
        }

        public string DeviceName
        {
            get;
            private set;
        }

        public int DeviceID
        {
            get;
            private set;
        }

        public string FirmwareVersion
        {
            get;
            private set;
        }

        public int LEDs
        {
            get { return _leds; }
            set
            {
                _leds = value;
                if (IsAvailable)
                {
                    SiApp.SiSetLEDs(_deviceHandle, _leds);
                }
            }
        }

        public string IconPath
        {
            get
            {
                if (IsAvailable)
                {
                    string path;
                    var ret = SiApp.SiGetDeviceImageFileName(_deviceHandle, out path);
                    return path;
                }
                return null;
            }
        }

        public void InitDevice(IntPtr windowHandle)
        {
            if (this.IsDisposed)
                throw new ObjectDisposedException("");
            if (IsAvailable)
                return; //Init already done.

            var v = SiApp.SiInitialize();
            if (v == SiApp.SpwRetVal.SPW_DLL_LOAD_ERROR)
                throw new _3DxException("Unable to load SiApp DLL");

            SiApp.SiOpenData o = default(SiApp.SiOpenData);
            SiApp.SiOpenWinInit(ref o, windowHandle);

            _deviceHandle = SiApp.SiOpen(this.AppName, SiApp.SI_ANY_DEVICE, IntPtr.Zero, SiApp.SI_EVENT, ref o);
            if (_deviceHandle == IntPtr.Zero)
            {
                SiApp.SiTerminate();
                throw new _3DxException("Unable to open device");
            }
            
            string devName;
            SiApp.SiGetDeviceName(_deviceHandle, out devName);

            this.DeviceName = devName;

            this.DeviceID = SiApp.SiGetDeviceID(_deviceHandle);

            SiApp.SiDevInfo info = default(SiApp.SiDevInfo);
            SiApp.SiGetDeviceInfo(_deviceHandle, ref info);

            this.FirmwareVersion = info.firmware;
        }

        public void CloseDevice()
        {
            if (_deviceHandle != IntPtr.Zero)
            {
                SiApp.SiClose(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
        }

        public void ProcessWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (this.IsDisposed) //We don't throw an Exception in the Message Loop, just return
                return;

            SiApp.SiGetEventData evd = default(SiApp.SiGetEventData);
            SiApp.SiGetEventWinInit(ref evd, msg, wParam, lParam);
            SiApp.SiSpwEvent_SpwData edata = default(SiApp.SiSpwEvent_SpwData);
            var t = SiApp.SiGetEvent(_deviceHandle, SiApp.SI_AVERAGE_EVENTS, ref evd, ref edata);
            if (t == SiApp.SpwRetVal.SI_IS_EVENT)
            {
                switch (edata.type)
                {
                    case SiApp.SiEventType.SI_ZERO_EVENT:
                        lock (eventBuffer)
                        {
                            eventBuffer[SiApp.SiEventType.SI_ZERO_EVENT] = EventArgs.Empty;
                            Monitor.Pulse(eventBuffer);
                        }
                        break;
                    case SiApp.SiEventType.SI_MOTION_EVENT:
                        lock (eventBuffer)
                        {
                            eventBuffer[SiApp.SiEventType.SI_MOTION_EVENT] = MotionEventArgs.FromEventArray(edata.spwData.mData);
                            Monitor.Pulse(eventBuffer);
                        }
                        break;
                }
            }
        }

        protected virtual void OnZeroPoint()
        {
            if (ZeroPoint != null)
                ZeroPoint(this, EventArgs.Empty);
        }

        protected virtual void OnMotion(MotionEventArgs args)
        {
            if (Motion != null)
                Motion(this, args);
        }

        protected virtual void OnDeviceChange(DeviceChangeEventArgs args)
        {
            if (DeviceChange != null)
                DeviceChange(this, args);
        }

        #region IDisposable Member

        public void Dispose()
        {
            if (!this.IsDisposed)
            {
                CloseDevice();
                ZeroPoint = null;
                Motion = null;
            }
            this.IsDisposed = true;
        }

        #endregion

        private void EventThreadLoop()
        {
            while (!this.IsDisposed)
            {
                lock (eventBuffer)
                {
                    while (eventBuffer.Count == 0)
                        Monitor.Wait(eventBuffer);
                    foreach (var c in eventBuffer)
                    {
                        switch (c.Key)
                        {
                            case SiApp.SiEventType.SI_MOTION_EVENT:
                                OnMotion(c.Value as MotionEventArgs); break;
                            case SiApp.SiEventType.SI_ZERO_EVENT:
                                OnZeroPoint(); break;
                        }
                    }
                    eventBuffer.Clear();
                }
                Thread.Sleep(50);
            }
        }
    }
}