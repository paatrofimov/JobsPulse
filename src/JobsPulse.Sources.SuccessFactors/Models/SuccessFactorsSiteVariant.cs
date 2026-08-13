namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// Which generation of external career site serves a tenant. SuccessFactors has no single public jobs api, and the
/// two generations do not even share a host: the modern one lives on the company's own domain, the legacy one on a
/// data center host of SAP. Kept in the board configuration so nothing has to sniff the site again on every poll.
/// </summary>
public enum SuccessFactorsSiteVariant
{
    /// <summary>
    /// Career Site Builder / Recruiting Marketing - a branded domain that publishes the whole board as one rss feed.
    /// </summary>
    CareerSiteBuilder,

    /// <summary>
    /// The legacy RCM career portal ('career{N}.successfactors.com/career?company=X'). It renders no job rows at all
    /// without a browser - see <see cref="SuccessFactorsSiteVariant"/> users for what that costs.
    /// </summary>
    LegacyCareerPortal
}
