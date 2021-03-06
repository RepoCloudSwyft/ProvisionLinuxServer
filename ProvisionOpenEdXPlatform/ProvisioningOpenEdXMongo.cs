using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Storage.Fluent;

namespace ProvisionOpenEdXPlatform
{
    public static class ProvisioningOpenEdXMongo
    {
        [FunctionName("ProvisioningOpenEdXMongo")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{Utils.DateAndTime()} | C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            ProvisioningModel provisioningModel = JsonConvert.DeserializeObject<ProvisioningModel>(requestBody);


            if (string.IsNullOrEmpty(provisioningModel.ClientId) ||
                string.IsNullOrEmpty(provisioningModel.ClientSecret) ||
                string.IsNullOrEmpty(provisioningModel.TenantId) ||
                string.IsNullOrEmpty(provisioningModel.SubscriptionId) ||
                string.IsNullOrEmpty(provisioningModel.ClustrerName) ||
                string.IsNullOrEmpty(provisioningModel.ResourceGroupName) ||
                string.IsNullOrEmpty(provisioningModel.MainVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.MysqlVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.MongoVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.SmtpServer) ||
                string.IsNullOrEmpty(provisioningModel.SmtpPort.ToString()) ||
                string.IsNullOrEmpty(provisioningModel.SmtpEmail) ||
                string.IsNullOrEmpty(provisioningModel.SmtpPassword))
            {
                log.LogInformation($"{Utils.DateAndTime()} | Error |  Missing parameter | \n{requestBody}");
                return new BadRequestObjectResult(false);
            }
            else
            {
                try
                {

                    string resourceGroupName = provisioningModel.ResourceGroupName;
                    string clusterName = provisioningModel.ClustrerName;
                    string MainVhdURL = provisioningModel.MainVhdURL;
                    string MysqlVhdURL = provisioningModel.MysqlVhdURL;
                    string MongoVhdURL = provisioningModel.MongoVhdURL;
                    string subnet = "default";
                    string username = provisioningModel.Username;
                    string password = provisioningModel.Password;

                    string contactPerson = provisioningModel.SmtpEmail;

                    log.LogInformation("deploying Mongo instance");
                    //Utils.Email(smtpClient, "Main Instance Deployed Successfully", log, mailMessage);

                    ServicePrincipalLoginInformation principalLogIn = new ServicePrincipalLoginInformation();
                    principalLogIn.ClientId = provisioningModel.ClientId;
                    principalLogIn.ClientSecret = provisioningModel.ClientSecret;

                    AzureEnvironment environment = AzureEnvironment.AzureGlobalCloud;
                    AzureCredentials credentials = new AzureCredentials(principalLogIn, provisioningModel.TenantId, environment);

                    IAzure _azureProd = Azure.Configure()
                          .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                          .Authenticate(credentials)
                          .WithSubscription(provisioningModel.SubscriptionId);


                    IResourceGroup resourceGroup = _azureProd.ResourceGroups.GetByName(resourceGroupName);
                    Region region = resourceGroup.Region;

                    #region mongo

                    INetwork virtualNetwork = _azureProd.Networks.GetByResourceGroup(resourceGroupName, $"{clusterName}-vnet");
                    log.LogInformation($"{Utils.DateAndTime()} | Detected | Virtual Network");

                    IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup(resourceGroupName, $"{clusterName}vhdsa");
                    log.LogInformation($"{Utils.DateAndTime()} | Detected | Storage Account");

                    INetworkSecurityGroup networkSecurityGroupmongo = _azureProd.NetworkSecurityGroups.Define($"{clusterName}-mongo-nsg")
                            .WithRegion(region)
                            .WithExistingResourceGroup(resourceGroup)
                            .DefineRule("ALLOW-SSH")
                                .AllowInbound()
                                .FromAnyAddress()
                                .FromAnyPort()
                                .ToAnyAddress()
                                .ToPort(22)
                                .WithProtocol(SecurityRuleProtocol.Tcp)
                                .WithPriority(100)
                                .WithDescription("Allow SSH")
                                .Attach()
                            .DefineRule("mongodb")
                                .AllowInbound()
                                .FromAnyAddress()
                                .FromAnyPort()
                                .ToAnyAddress()
                                .ToPort(27017)
                                .WithProtocol(SecurityRuleProtocol.Tcp)
                                .WithPriority(101)
                                .WithDescription("mongodb")
                                .Attach()
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                    log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Network Security Group");

                    INetworkInterface networkInterfacemongo = _azureProd.NetworkInterfaces.Define($"{clusterName}-mongo-nic")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet(subnet)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingNetworkSecurityGroup(networkSecurityGroupmongo)
                        .WithTag("_contact_person", contactPerson)
                        .Create();

                    log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Network Interface");

                    IVirtualMachine createVmmongo = _azureProd.VirtualMachines.Define($"{clusterName}-mongo")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetworkInterface(networkInterfacemongo)
                        .WithStoredLinuxImage(MongoVhdURL)
                        .WithRootUsername(username)
                        .WithRootPassword(password)
                        .WithComputerName("mongo")
                        .WithBootDiagnostics(storageAccount)
                        .WithSize(VirtualMachineSizeTypes.StandardD2V2)
                        .WithTag("_contact_person", contactPerson)
                        .Create();

                    log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Virtual Machine");
                    #endregion
                }
                catch (Exception e)
                {
                    log.LogInformation($"{Utils.DateAndTime()} | Error | {e.Message}");

                    return new BadRequestObjectResult(false);
                }

                log.LogInformation($"{Utils.DateAndTime()} | Done");
                return new OkObjectResult(true);
            }
        }
    }
}