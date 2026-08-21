using Azure.Core;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Zava.SalesIntelligence.Dataverse;

// Builds a Dataverse client authenticated with an Entra ID credential. For phase 1
// the credential is the interactive az session; application users arrive in phase 4.
public static class DataverseClientFactory
{
    public static ServiceClient Create(string environmentUrl, TokenCredential credential)
    {
        var scope = $"{environmentUrl.TrimEnd('/')}/.default";
        return new ServiceClient(
            instanceUrl: new Uri(environmentUrl),
            tokenProviderFunction: async _ =>
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext([scope]), CancellationToken.None);
                return token.Token;
            });
    }
}
