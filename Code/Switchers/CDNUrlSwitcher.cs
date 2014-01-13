namespace NTTData.SitecoreCDN.Switchers
{
    using Sitecore.Common;

    public class CDNUrlSwitcher : Switcher<CDNUrlState>
    {
        public CDNUrlSwitcher(CDNUrlState state) : base(state) { }
    }
}
