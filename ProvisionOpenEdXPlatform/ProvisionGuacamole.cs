using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Mail;

using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.TrafficManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core.ResourceActions;
using Microsoft.Azure.Management.TrafficManager.Fluent.TrafficManagerProfile.Definition;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using System.Net;

namespace ProvisionOpenEdXPlatform
{
    public static class ProvisionGuacamole
    {

        [FunctionName("ProvisionGuacamole")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

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
                string.IsNullOrEmpty(provisioningModel.SmtpEmail) ||
                string.IsNullOrEmpty(provisioningModel.Username) ||
                string.IsNullOrEmpty(provisioningModel.Password))
            {
                log.LogInformation($"{Utils.DateAndTime()} | Error |  Missing parameter | \n{requestBody}");
                return new BadRequestObjectResult(false);
            }

            try {
                string resourceGroupName = provisioningModel.ResourceGroupName;
                string clusterName = provisioningModel.ClustrerName;
                string MainVhdURL = provisioningModel.MainVhdURL;
                string subnet = "default";
                string username = provisioningModel.Username;
                string password = provisioningModel.Password;

                string contactPerson = provisioningModel.SmtpEmail;

                log.LogInformation("deploying Main instance");
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

                log.LogInformation($"{Utils.DateAndTime()} | INF | Creating VNet");
                #region Create Virtual Network
                INetwork virtualNetwork = _azureProd.Networks.Define($"{clusterName}-vnet")
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithAddressSpace("10.0.0.0/16")
                    .DefineSubnet(subnet)
                        .WithAddressPrefix("10.0.0.0/24")
                        .Attach()
                    .WithTag("_contact_person", contactPerson)
                    .Create();
                #endregion
                log.LogInformation($"{Utils.DateAndTime()} | INF | Created VNet");

                log.LogInformation($"{Utils.DateAndTime()} | INF | Creating VM IP Address");
                #region Create VM IP
                IPublicIPAddress publicIpAddress = _azureProd.PublicIPAddresses.Define($"{clusterName}-vm-ip")
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithDynamicIP()
                    .WithLeafDomainLabel(clusterName)
                    .WithTag("_contact_person", contactPerson)
                    .Create();
                #endregion
                log.LogInformation($"{Utils.DateAndTime()} | INF | Created VM IP Address");

                log.LogInformation($"{Utils.DateAndTime()} | INF | Creating Network Security Group");
                #region NSG
                INetworkSecurityGroup networkSecurityGroup = _azureProd.NetworkSecurityGroups.Define($"{clusterName}-nsg")
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .DefineRule("SSH")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(22)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(100)
                        .WithDescription("SSH")
                        .Attach()
                    .DefineRule("HTTPS")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(443)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(101)
                        .WithDescription("HTTPS")
                        .Attach()
                    .DefineRule("RDP")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(3389)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(102)
                        .WithDescription("RDP")
                        .Attach()
                    .DefineRule("MySQL")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(3306)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(112)
                        .WithDescription("MySQL")
                        .Attach()
                    .DefineRule("Port8080")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(8080)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(122)
                        .WithDescription("Port8080")
                        .Attach()
                    .WithTag("_contact_person", contactPerson)
                    .Create();
                #endregion
                log.LogInformation($"{Utils.DateAndTime()} | INF | Created Network Security Group");

                log.LogInformation($"{Utils.DateAndTime()} | INF | Creating Network Interface");
                #region nic
                INetworkInterface networkInterface = _azureProd.NetworkInterfaces.Define($"{clusterName}-nic")
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithExistingPrimaryNetwork(virtualNetwork)
                    .WithSubnet(subnet)
                    .WithPrimaryPrivateIPAddressDynamic()
                    .WithExistingPrimaryPublicIPAddress(publicIpAddress)
                    .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                    .WithTag("_contact_person", contactPerson)
                    .Create();
                #endregion
                log.LogInformation($"{Utils.DateAndTime()} | INF | Created Network Interface");

                log.LogInformation($"{Utils.DateAndTime()} | INF | Checking storage account");
                IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup(resourceGroupName, $"{clusterName}vhdsa");

                log.LogInformation($"{Utils.DateAndTime()} | INF | Creating Main Virtual Machine");
                #region vm
                IVirtualMachine createVm = _azureProd.VirtualMachines.Define($"{clusterName}-jb")
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithExistingPrimaryNetworkInterface(networkInterface)
                    .WithStoredLinuxImage(MainVhdURL)
                    .WithRootUsername(username)
                    .WithRootPassword(password)
                    .WithComputerName(username)
                    .WithBootDiagnostics(storageAccount)
                    .WithSize(VirtualMachineSizeTypes.StandardB2ms)
                    .WithTag("_contact_person", contactPerson)
                    .Create();
                #endregion
                log.LogInformation($"{Utils.DateAndTime()} | INF | Created Main Virtual Machine");

                MailMessage message = new MailMessage();

                string subject = $"Guacamole deployment";
                string htmlString =
                    "<br/>Your guacamole is ready to use.<br/>" +
                    $"<br/><a href=\"http://{publicIpAddress.Fqdn}:8080/guacamole/#/\">Guacamole<a/><br/>" +
                    "<br/><br/>";
                string attachmentPath = string.Empty;

                Utils.Email(htmlString, log ,message, subject);
                return new OkObjectResult(JsonConvert.SerializeObject(new { 
                    virtualMachineName = createVm.Name,
                }));
            }
            catch (Exception e) 
            {
                return new BadRequestObjectResult(
                    JsonConvert.SerializeObject(new { 
                        message = e.Message
                    }));
            }

        }
    }
}
