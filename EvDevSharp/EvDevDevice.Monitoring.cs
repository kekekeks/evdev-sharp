using System.Runtime.InteropServices;
using EvDevSharp.InteropStructs;

namespace EvDevSharp;

public unsafe sealed partial class EvDevDevice : IDisposable
{
    private Task? monitoringTask;
    private CancellationTokenSource? cts;

    /// <summary>
    /// This method starts to read the device's event file on a separate thread and will raise events accordingly.
    /// </summary>
    public void StartMonitoring()
    {
        if (cts is not null && !cts.IsCancellationRequested)
            return;

        cts = new();
        monitoringTask = Task.Run(Monitor);

        void Monitor()
        {
            InputEvent inputEvent;
            int size = Marshal.SizeOf(typeof(InputEvent));
            byte[] buffer = new byte[size];

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            using var eventFile = File.OpenRead(this.DevicePath);
            while (!cts.Token.IsCancellationRequested)
            {
                eventFile.Read(buffer, 0, size);
                inputEvent = (InputEvent)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(InputEvent))!;
                switch ((EvDevEventType)inputEvent.type)
                {
                    case EvDevEventType.EV_SYN:
                        OnSynEvent?.Invoke(this, new OnSynEventArgs((EvDevSynCode)inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_KEY:
                        OnKeyEvent?.Invoke(this, new OnKeyEventArgs((EvDevKeyCode)inputEvent.code, (EvDevKeyValue)inputEvent.value));
                        break;

                    case EvDevEventType.EV_REL:
                        OnRelativeEvent?.Invoke(this, new OnRelativeEventArgs((EvDevRelativeAxisCode)inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_ABS:
                        OnAbsoluteEvent?.Invoke(this, new OnAbsoluteEventArgs((EvDevAbsoluteAxisCode)inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_MSC:
                        OnMiscellaneousEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_SW:
                        OnSwitchEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_LED:
                        OnLedEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_SND:
                        OnSoundEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_REP:
                        OnAutoRepeatEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_FF:
                        OnForceFeedbackEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_PWR:
                        OnPowerEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;

                    case EvDevEventType.EV_FF_STATUS:
                        OnForceFeedbackStatusEvent?.Invoke(this, new EvDevEventArgs(inputEvent.code, inputEvent.value));
                        break;
                }
            }

            handle.Free();
        }
    }

    /// <summary>
    /// This method cancels event file reading for this device.
    /// </summary>
    public void StopMonitoring()
    {
        cts?.Cancel();
        monitoringTask?.Wait();
    }

    public void Dispose() => StopMonitoring();

    ~EvDevDevice() => StopMonitoring();
}