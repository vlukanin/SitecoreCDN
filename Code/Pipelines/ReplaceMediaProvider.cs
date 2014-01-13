namespace NTTData.SitecoreCDN.Pipelines
{
    using NTTData.SitecoreCDN.Configuration;
    using NTTData.SitecoreCDN.Providers;
    using Sitecore.Diagnostics;
    using Sitecore.Pipelines;
    using Sitecore.Resources.Media;
    
    /// <summary>
    /// Injects the CDN replacement media provider into the MediaManager
    /// </summary>
    public class ReplaceMediaProvider
    {
        public void Process(PipelineArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (CDNSettings.Enabled)
            {
                MediaManager.Provider = new CDNMediaProvider();
            }
        }
    }
}
