namespace NTTData.SitecoreCDN.Caching
{
    using System;
    using Sitecore.Caching;
    using Sitecore.Configuration;

    /// <summary>
    /// This cache keeps mappings of original urls to dehydrated cdn urls
    /// /path/to/file.ext?a=1&b=2  => http://cdnhostname/path/to/file!cf!a=1!b=2.ext
    /// </summary>
    public class UrlCache: CustomCache
    {
        private TimeSpan _cacheTime;

        public UrlCache(string name, long maxSize)
            : base(name, maxSize)
        {
            this.InnerCache.Scavengable = true;
            this._cacheTime = Settings.GetTimeSpanSetting("SitecoreCDN.UrlVersionCacheTime", "00:05:00");
        }


        public string GetUrl(string path)
        {
            return this.GetString(path);
        }

        public void SetUrl(string path, string url)
        {
            this.SetString(path, url, DateTime.UtcNow.Add(this._cacheTime));
        }
    }
}
