﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;

namespace MSIHelperService.Helper
{
    internal static class MSIHelper
    {
        //content type
        internal const string ContentTypeJson = "application/json";
        internal const string ContentTypeTextOrHtml = "text/html";
        internal const string ContentTypeMulipartOrFormData = "multipart/form-data";

        //default azure resource if nothing is passed in the controllers
        internal const string DefaultAzureResource = "WebApp";

        //IDENTITY_HEADER in the App Service
        internal const string ManagedIdentityAuthenticationHeader = "X-IDENTITY-HEADER";

        //Environment variables
        internal static readonly string? s_requestAppID = Environment.GetEnvironmentVariable("requestAppID");
        internal static readonly string? s_requestAppSecret = Environment.GetEnvironmentVariable("requestAppSecret");
        internal static readonly string? s_functionAppUri = Environment.GetEnvironmentVariable("functionAppUri");
        internal static readonly string? s_functionAppEnvCode = Environment.GetEnvironmentVariable("functionAppEnvCode");
        internal static readonly string? s_functionAppMSICode = Environment.GetEnvironmentVariable("functionAppMSICode");
        internal static readonly string? s_webhookLocation = Environment.GetEnvironmentVariable("webhookLocation");
        internal static readonly string? s_oMSAdminClientID = Environment.GetEnvironmentVariable("OMSAdminClientID");
        internal static readonly string? s_oMSAdminClientSecret = Environment.GetEnvironmentVariable("OMSAdminClientSecret");

        //Microsoft authority endpoint
        internal const string Authority = "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47";

        //OMS Runbook 
        internal const string LabSubscription = "https://management.azure.com/subscriptions/c1686c51-b717-4fe0-9af3-24a20a41fb0c/";
        internal const string RunbookLocation = "resourceGroups/OperationsManagementSuite/";
        internal const string RunbookJobProvider = "providers/Microsoft.Automation/automationAccounts/OMSAdmin/jobs/";
        internal const string AzureRunbook = LabSubscription + RunbookLocation + RunbookJobProvider;
        internal const string RunbookAPIVersion = "2019-06-01";

        //Azure Resources
        internal enum AzureResource
        {
            WebApp,
            Function,
            VM,
            AzureArc,
            ServiceFabric,
            CloudShell
        }

        /// <summary>
        /// Gets the Environment Variables from the Azure Web App
        /// </summary>
        /// <param name="logger"></param>
        /// <returns>Returns the environment variables</returns>
        public static async Task<Dictionary<string, string>> GetWebAppEnvironmentVariablesAsync(
            ILogger logger)
        {
            //Gets Azure Web App Specific environment variables and sends it back
            //Sending back the specific ones that is needed for the MSI tests
            Dictionary<string, string>? keyValuePairs = new();
            keyValuePairs.Add("IDENTITY_HEADER", Environment.GetEnvironmentVariable("IDENTITY_HEADER") ?? "");
            keyValuePairs.Add("IDENTITY_ENDPOINT", Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT") ?? "");
            keyValuePairs.Add("IDENTITY_API_VERSION", Environment.GetEnvironmentVariable("IDENTITY_API_VERSION") ?? "");

            logger.LogInformation("GetWebAppEnvironmentVariables Function called.");

            return await Task.FromResult(keyValuePairs).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the Environment Variable from the Azure Function
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="logger"></param>
        /// <returns>Returns the environment variables</returns>
        public static async Task<Dictionary<string, string>?> GetFunctionAppEnvironmentVariablesAsync(
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("GetFunctionAppEnvironmentVariables Function called.");

            string[] scopes = new string[] { "https://request.msidlab.com/.default" };

            string? token = await GetMSALToken(s_requestAppID, s_requestAppSecret, scopes, logger)
                .ConfigureAwait(false);

            //clear the default request header for each call
            ClearDefaultRequestHeaders(logger, httpClient);

            //Set the Authorization header
            SetAuthorizationHeader(token, httpClient, logger);

            //send the request
            HttpResponseMessage result = await httpClient
            .GetAsync(s_functionAppUri + 
            "GetEnvironmentVariables?code=" 
            + s_functionAppEnvCode)
            .ConfigureAwait(false);

            var content = await result.Content.ReadAsStringAsync().ConfigureAwait(false);

            Dictionary<string, string>? envValuePairs = JsonSerializer.Deserialize<Dictionary<string, string>>(content);

            logger.LogInformation("GetFunctionAppEnvironmentVariables call was successful.");

            return envValuePairs;
        }

        /// <summary>
        /// Gets the Environment Variables for IMDS
        /// </summary>
        /// <param name="logger"></param>
        /// <returns>Returns the environment variable</returns>
        public static async Task<Dictionary<string, string>> GetVirtualMachineEnvironmentVariables(
            ILogger logger)
        {
            //IMDS endpoint has only one environment variable and the VMs do not have this
            //MSAL .Net has this value hardcoded for now. So sending a set value

            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
            keyValuePairs.Add("AZURE_POD_IDENTITY_AUTHORITY_HOST", "http://169.254.169.254/metadata/identity/oauth2/token");
            keyValuePairs.Add("IMDS_API_VERSION", "2018-02-01");

            logger.LogInformation("GetVirtualMachineEnvironmentVariables Function called.");

            return await Task.FromResult(keyValuePairs).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the MSI Token from the Azure Web App
        /// </summary>
        /// <param name="identityHeader"></param>
        /// <param name="uri"></param>
        /// <param name="httpClient"></param>
        /// <param name="logger"></param>
        /// <returns>Returns MSI Token</returns>
        public static async Task<ActionResult?> GetWebAppMSIToken(
            string? identityHeader,
            string? uri,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("GetWebAppMSIToken Function called.");

            var decodedUri = HttpUtility.UrlDecode(uri);

            //set the http get method and the required headers for a web app
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, decodedUri);
            requestMessage.Headers.Add(ManagedIdentityAuthenticationHeader, identityHeader);

            //clear the default request header for each call
            ClearDefaultRequestHeaders(logger, httpClient);

            //send the request
            HttpResponseMessage? result = await httpClient
                .SendAsync(requestMessage)
                .ConfigureAwait(false);

            string body = await result.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            logger.LogInformation("GetWebAppMSIToken Function call was successful.");

            return GetContentResult(body, "application/json", (int)result.StatusCode);
        }

        /// <summary>
        /// Gets the MSI Token from the Azure Function App
        /// </summary>
        /// <param name="identityHeader"></param>
        /// <param name="uri"></param>
        /// <param name="httpClient"></param>
        /// <param name="logger"></param>
        /// <returns>Returns MSI Token</returns>
        public static async Task<ActionResult?> GetFunctionAppMSIToken(
            string? identityHeader,
            string uri,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("GetFunctionAppMSIToken Function called.");

            //Scopes
            string[] scopes = new string[] { "https://request.msidlab.com/.default" };

            string? token = await GetMSALToken(s_requestAppID, s_requestAppSecret, scopes, logger)
                .ConfigureAwait(false);

            //clear the default request header for each call
            ClearDefaultRequestHeaders(logger, httpClient);

            //Set the Authorization header
            SetAuthorizationHeader(token, httpClient, logger);

            //send the request
            var encodedUri = HttpUtility.UrlEncode(uri);

            HttpResponseMessage result = await httpClient.GetAsync(s_functionAppUri + "getToken?code=" +
                s_functionAppMSICode + "&uri=" + encodedUri + "&header=" +identityHeader)
                .ConfigureAwait(false);

            string body = await result.Content.ReadAsStringAsync()
                .ConfigureAwait(false);

            logger.LogInformation("GetFunctionAppMSIToken call was successful.");

            return GetContentResult(body, "application/json", (int)result.StatusCode);
        }

        /// <summary>
        /// Gets the MSI Token from the Azure Virtual Machine
        /// </summary>
        /// <param name="identityHeader"></param>
        /// <param name="uri"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static async Task<ActionResult?> GetVirtualMachineMSIToken(
            string? identityHeader,
            string uri,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("GetVirtualMachineMSIToken Function called.");
            string response;
            HttpResponseMessage? responseMessage = new HttpResponseMessage();

            try
            {
                //Scopes
                string[] scopes = new string[] { "https://management.core.windows.net/.default" };

                //get the msal token for the client
                string? token = await GetMSALToken(
                    s_oMSAdminClientID,
                    s_oMSAdminClientSecret,
                    scopes,
                    logger).ConfigureAwait(false);

                //clear the default request header for each call
                ClearDefaultRequestHeaders(logger, httpClient);

                //Set the Authorization header
                SetAuthorizationHeader(token, httpClient, logger);

                //Set additional headers
                SetAdditionalHeader("MSI_Identity_Header", identityHeader, httpClient, logger);
                SetAdditionalHeader("MSI_URI", uri.ToString(), httpClient, logger);

                //get the job id
                string? jobId = await StartAzureRunbookandGetJobId(httpClient, logger)
                    .ConfigureAwait(false);

                if (await AzureRunbookJobStatusIsCompleted(jobId, httpClient, logger).ConfigureAwait(false))
                {
                    responseMessage = await httpClient.
                        GetAsync(
                        AzureRunbook +
                        jobId +
                        "/output?api-version=" +
                        RunbookAPIVersion).ConfigureAwait(false);

                    //send back the response
                    response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

                    logger.LogInformation("GetVirtualMachineMSIToken call was successful.");
                }
                else
                {
                    logger.LogError("Runbook failed to get MSI Token.");

                    //Runbook failed
                    response = "Azure Runbook failed to execute.";
                }

                return GetContentResult(response, "application/json", (int)responseMessage.StatusCode);
            }
            catch (Exception ex)
            {
                //Catch the Azure Runbook exception
                var errorResponse = ex.Message;

                logger.LogError("GetVirtualMachineMSIToken call failed.");
                
                return GetContentResult(errorResponse, "application/json", (int)responseMessage.StatusCode);
            }
        }

        /// <summary>
        /// Get Azure Runbook Job Status
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="logger"></param>
        /// <returns>Azure runbook job Status</returns>
        private static async Task<bool> AzureRunbookJobStatusIsCompleted(
            string? jobId,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("AzureRunbookJobStatusIsCompleted Function called.");

            //Get the Job status
            HttpResponseMessage? jobStatus;
            RunBookJobStatus? runBookJobStatus;
            string? currentJobStatus;

            do
            {
                //get the current job status based on the job id
                jobStatus = await httpClient.GetAsync(
                        AzureRunbook +
                        jobId +
                        "?api-version=" +
                        RunbookAPIVersion).ConfigureAwait(false);

                //get the status
                runBookJobStatus = await jobStatus.
                    Content
                    .ReadFromJsonAsync<RunBookJobStatus>()
                    .ConfigureAwait(false);

                currentJobStatus = runBookJobStatus?.Properties?.Status;

                //catch runbook failure 
                if (currentJobStatus == "Failed")
                {
                    return false;
                }

                logger.LogInformation($"Current Job Status is - { currentJobStatus }.");
            }
            while (currentJobStatus != "Completed");

            return true;
        }

        /// <summary>
        /// Starts the Runbook and gets Azure Runbook Job Id 
        /// </summary>
        /// <param name="logger"></param>
        /// <returns>Azure runbook job ID</returns>
        private static async Task<string?> StartAzureRunbookandGetJobId(HttpClient httpClient, ILogger logger)
        {
            logger.LogInformation("StartAzureRunbookandGetJobId Function called.");

            string payload = "";

            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            //invoke the azure runbook from the webhook
            HttpResponseMessage? invokeWebHook = await httpClient
                .PostAsJsonAsync(s_webhookLocation,content)
                .ConfigureAwait(false);

            //Get the Job ID
            WebHookResponse? jobResponse = await invokeWebHook.Content
                .ReadFromJsonAsync<WebHookResponse>()
                .ConfigureAwait(false);

            string? jobId = jobResponse?.JobIDs?[0];

            if (!string.IsNullOrEmpty(jobId))
            {
                logger.LogInformation("Job ID retrieved from the Azure Runbook.");
                logger.LogInformation($"Job Id is - { jobId }.");
            }
            else
            {
                logger.LogError("Failed to get Job ID from the Azure Runbook.");
            }

            return jobId;
        }

        /// <summary>
        /// Sets the additional headers on the http client
        /// </summary>
        /// <param name="token"></param>
        /// <param name="token"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static void SetAdditionalHeader(
            string key,
            string? value,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("SetAdditionalHeader Function called.");

            if (httpClient != null)
            {
                httpClient.DefaultRequestHeaders.Add(key, value);
            }
        }

        /// <summary>
        /// Get the Client Token 
        /// </summary>
        /// <param name="appID"></param>
        /// <param name="appSecret"></param>
        /// <param name="scopes"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static async Task<string?> GetMSALToken(
            string? clientID, 
            string? secret, 
            string[] scopes, 
            ILogger logger)
        {
            logger.LogInformation("GetMSALToken Function called.");

            //Confidential Client Application Builder
            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(clientID)
           .WithClientSecret(secret)
           .WithAuthority(new Uri(Authority))
           .WithCacheOptions(CacheOptions.EnableSharedCacheOptions)
           .Build();

            //Acquire Token For Client using MSAL
            try
            {
                AuthenticationResult result = await app.AcquireTokenForClient(scopes)
                    .ExecuteAsync()
                    .ConfigureAwait(false);

                logger.LogInformation("MSAL Token acquired successfully.");
                logger.LogInformation($"MSAL Token source is : { result.AuthenticationResultMetadata.TokenSource }");
                
                return result.AccessToken;
            }
            catch (MsalException ex)
            {
                logger.LogError(ex.Message);
                return ex.Message;
            }
        }

        /// <summary>
        /// Clear default request headers on the http client
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="httpClient"></param>
        /// <returns></returns>
        private static void ClearDefaultRequestHeaders(
            ILogger logger, 
            HttpClient httpClient)
        {
            logger.LogInformation("ClearDefaultRequestHeaders Function called.");

            if (httpClient != null)
                httpClient.DefaultRequestHeaders.Clear();
        }

        /// <summary>
        /// Sets the authorization header on the http client
        /// </summary>
        /// <param name="token"></param>
        /// <param name="httpClient"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static void SetAuthorizationHeader(
            string? token,
            HttpClient httpClient,
            ILogger logger)
        {
            logger.LogInformation("SetAuthorizationHeader Function called.");

            if (httpClient != null)
            {
                httpClient.DefaultRequestHeaders.Authorization
                    = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        /// <summary>
        /// Returns Content Result for final output from the web api
        /// </summary>
        /// <param name="content"></param>
        /// <param name="contentEncoding"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        private static ContentResult GetContentResult(
            string content, 
            string contentEncoding, 
            int statusCode)
        {
            return new ContentResult
            {
                Content = content,
                ContentType = contentEncoding,
                StatusCode = statusCode
            };
        }
    }
}
