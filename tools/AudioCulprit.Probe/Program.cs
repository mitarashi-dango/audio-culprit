using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using var devices = new MMDeviceEnumerator();
using var output = new WasapiOut();
output.Init(new SignalGenerator { Gain = 0.025, Frequency = 660 }.Take(TimeSpan.FromSeconds(0.3)));
output.Play();
var found = false;
var until = DateTime.UtcNow.AddSeconds(2);
while (DateTime.UtcNow < until)
{
    foreach (var d in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
    {
        using (d)
        {
            var sessions = d.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var s = sessions[i];
                var peak = s.AudioMeterInformation.MasterPeakValue;
                if (peak > 0.001)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} PID={s.GetProcessID} Peak={peak:F4} Device={d.FriendlyName}");
                    if (s.GetProcessID == Environment.ProcessId)
                        found = true;
                }
            }
        }
    }
    Thread.Sleep(50);
}
Console.WriteLine(found ? "PASS: own real render session detected" : "FAIL: no probe session peak");
return found ? 0 : 1;
