﻿using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Internal.Broker;
using Microsoft.Identity.Client.OAuth2;
using Microsoft.Identity.Client.Utils;
using Microsoft.Identity.Test.Common.Core.Helpers;
using Microsoft.Identity.Test.Common.Core.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Microsoft.Identity.Test.Unit.BrokerTests
{
#if DESKTOP
    [TestClass]
    public class WamGetAccountsTests : TestBase
    {
        [TestMethod]
        public async Task WAM_AccountIdWriteback_Async()
        {
            // Arrange
            using (var httpManager = new MockHttpManager())
            {
                httpManager.AddInstanceDiscoveryMockHandler();

                var mockBroker = Substitute.For<IBroker>();
                mockBroker.IsBrokerInstalledAndInvokable().Returns(true);
                
                var msalTokenResponse = CreateMsalTokenResponseFromWam("wam1");
                mockBroker.AcquireTokenInteractiveAsync(null, null).ReturnsForAnyArgs(Task.FromResult(msalTokenResponse));

                var pca = PublicClientApplicationBuilder.Create(TestConstants.ClientId)
                    .WithBroker(true)
                    .WithHttpManager(httpManager)                    
                    .BuildConcrete();

                pca.ServiceBundle.PlatformProxy.SetBrokerForTest(mockBroker);

                // Act
                await pca.AcquireTokenInteractive(TestConstants.s_scope).ExecuteAsync().ConfigureAwait(false);
                var accounts = await pca.GetAccountsAsync().ConfigureAwait(false);

                // Assert
                var wamAccountIds = (accounts.Single() as IAccountInternal).WamAccountIds;
                Assert.AreEqual(1, wamAccountIds.Count);
                Assert.AreEqual("wam1", wamAccountIds[TestConstants.ClientId]);

                var pca2 = PublicClientApplicationBuilder.Create(TestConstants.ClientId2)
                    .WithBroker(true)
                    .WithHttpManager(httpManager)
                    .BuildConcrete();
                pca2.ServiceBundle.PlatformProxy.SetBrokerForTest(mockBroker);
                var accounts2 = await pca2.GetAccountsAsync().ConfigureAwait(false);
                Assert.IsFalse(accounts2.Any());
            }
        }

        [TestMethod]
        public async Task WAM_AccountIds_GetMerged_Async()
        {
            // Arrange
            using (var httpManager = new MockHttpManager())
            {
                var cache = new InMemoryTokenCache();
                httpManager.AddInstanceDiscoveryMockHandler();

                var mockBroker = Substitute.For<IBroker>();
                mockBroker.IsBrokerInstalledAndInvokable().Returns(true);

                var msalTokenResponse1 = CreateMsalTokenResponseFromWam("wam1");
                var msalTokenResponse2 = CreateMsalTokenResponseFromWam("wam2");
                var msalTokenResponse3 = CreateMsalTokenResponseFromWam("wam3");

                // 2 apps must share the token cache, like FOCI apps, for this test to be interesting
                var pca1 = PublicClientApplicationBuilder.Create(TestConstants.ClientId)
                    .WithBroker(true)
                    .WithHttpManager(httpManager)
                    .BuildConcrete();                

                var pca2 = PublicClientApplicationBuilder.Create(TestConstants.ClientId2)
                    .WithBroker(true)
                    .WithHttpManager(httpManager)
                    .BuildConcrete();

                cache.Bind(pca1.UserTokenCache);
                cache.Bind(pca2.UserTokenCache);

                pca1.ServiceBundle.PlatformProxy.SetBrokerForTest(mockBroker);
                pca2.ServiceBundle.PlatformProxy.SetBrokerForTest(mockBroker);

                // Act 
                mockBroker.AcquireTokenInteractiveAsync(null, null).ReturnsForAnyArgs(Task.FromResult(msalTokenResponse1));
                await pca1.AcquireTokenInteractive(TestConstants.s_scope).ExecuteAsync().ConfigureAwait(false);

                // this should override wam1 id
                mockBroker.AcquireTokenInteractiveAsync(null, null).ReturnsForAnyArgs(Task.FromResult(msalTokenResponse2));
                await pca1.AcquireTokenInteractive(TestConstants.s_scope).ExecuteAsync().ConfigureAwait(false);

                mockBroker.AcquireTokenInteractiveAsync(null, null).ReturnsForAnyArgs(Task.FromResult(msalTokenResponse3));
                await pca2.AcquireTokenInteractive(TestConstants.s_scope).ExecuteAsync().ConfigureAwait(false);

                var accounts1 = await pca1.GetAccountsAsync().ConfigureAwait(false);
                var accounts2 = await pca2.GetAccountsAsync().ConfigureAwait(false);

                // Assert
                var wamAccountIds = (accounts1.Single() as IAccountInternal).WamAccountIds;
                Assert.AreEqual(2, wamAccountIds.Count);
                Assert.AreEqual("wam2", wamAccountIds[TestConstants.ClientId]);
                Assert.AreEqual("wam3", wamAccountIds[TestConstants.ClientId2]);
                CoreAssert.AssertDictionariesAreEqual(wamAccountIds, (accounts2.Single() as IAccountInternal).WamAccountIds, StringComparer.Ordinal);
            }
        }

        [TestMethod]
        public async Task GetAccounts_Returns_Both_WAM_And_Cache_Accounts_Async()
        {
            // Arrange
            using (var httpManager = new MockHttpManager())
            {
                httpManager.AddInstanceDiscoveryMockHandler();
                string commonAccId = $"{TestConstants.Uid}.{TestConstants.Utid}";
                Account brokerAccount1 = new Account(commonAccId, "commonAccount", "login.windows.net");
                Account brokerAccount2 = new Account("other.account", "brokerAcc2", "login.windows.net");
                IEnumerable<IAccount> brokerAccounts = new List<IAccount>() { brokerAccount1, brokerAccount2 };

                var mockBroker = Substitute.For<IBroker>();
                mockBroker.IsBrokerInstalledAndInvokable().Returns(true);

                var msalTokenResponse = CreateMsalTokenResponseFromWam("wam_acc_id");
                mockBroker.AcquireTokenInteractiveAsync(null, null).ReturnsForAnyArgs(Task.FromResult(msalTokenResponse));
                mockBroker.GetAccountsAsync(null, null).ReturnsForAnyArgs(
                    Task.FromResult(brokerAccounts));

                var pca = PublicClientApplicationBuilder.Create(TestConstants.ClientId)
                    .WithBroker(true)
                    .WithHttpManager(httpManager)
                    .BuildConcrete();

                pca.ServiceBundle.PlatformProxy.SetBrokerForTest(mockBroker);

                // Act
                await pca.AcquireTokenInteractive(TestConstants.s_scope).ExecuteAsync().ConfigureAwait(false);
                var accounts = await pca.GetAccountsAsync().ConfigureAwait(false);

                // Assert
                Assert.AreEqual(2, accounts.Count());

                var wamAccountIds = (accounts.Single(acc => acc.HomeAccountId.Identifier == commonAccId) as IAccountInternal).WamAccountIds;
                Assert.AreEqual(1, wamAccountIds.Count);
                Assert.AreEqual("wam_acc_id", wamAccountIds[TestConstants.ClientId]);
            }
        }   

        private static MsalTokenResponse CreateMsalTokenResponseFromWam(string wamAccountId)
        {
            return new MsalTokenResponse
            {
                IdToken = MockHelpers.CreateIdToken(TestConstants.UniqueId, TestConstants.DisplayableId),
                AccessToken = "access-token",
                ClientInfo = MockHelpers.CreateClientInfo(),
                ExpiresIn = 3599,
                CorrelationId = "correlation-id",
                RefreshToken = null, // brokers don't return RT
                Scope = TestConstants.s_scope.AsSingleString(),
                TokenType = "Bearer", 
                WamAccountId = wamAccountId,
            };
        }
    }
#endif
}