using Azure.Core;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Zava.SalesIntelligence.Dataverse;

// Builds a Dataverse client authenticated with an Entra ID credential. SquadFactory builds two:
// one from the reader app user's credential and one from the writer's - the identity boundary
// is decided here, at the client layer, never inside an agent.
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
