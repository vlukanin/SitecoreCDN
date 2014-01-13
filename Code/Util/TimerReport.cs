namespace NTTData.SitecoreCDN.Util
{
    using System;
    using Sitecore.Diagnostics;

    public class TimerReport : IDisposable
    {
        private HighResTimer _timer;
        private string _name;

        public TimerReport(string name)
        {
            this._name = name;
            this._timer = new HighResTimer(true);
        }

        public void Dispose()
        {
            this._timer.Stop();
            System.Diagnostics.Debug.WriteLine(string.Format("{0} in {1}ms", this._name, this._timer.ElapsedTimeSpan.TotalMilliseconds));
        }
    }
}
