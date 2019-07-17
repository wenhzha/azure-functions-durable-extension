﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Primitives;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask.Tests
{
    public class DurableHttpTests : IDisposable
    {
        private readonly ITestOutputHelper output;

        private readonly TestLoggerProvider loggerProvider;
        private readonly bool useTestLogger = IsLogFriendlyPlatform();
        private readonly LogEventTraceListener eventSourceListener;

        public DurableHttpTests(ITestOutputHelper output)
        {
            this.output = output;
            this.loggerProvider = new TestLoggerProvider(output);
            this.eventSourceListener = new LogEventTraceListener();
            this.StartLogCapture();
        }

        public void Dispose()
        {
            this.eventSourceListener.Dispose();
        }

        private void OnEventSourceListenerTraceLog(object sender, LogEventTraceListener.TraceLogEventArgs e)
        {
            this.output.WriteLine($"      ETW: {e.ProviderName} [{e.Level}] : {e.Message}");
        }

        private void StartLogCapture()
        {
            if (this.useTestLogger)
            {
                var traceConfig = new Dictionary<string, TraceEventLevel>
                {
                    { "DurableTask-AzureStorage", TraceEventLevel.Informational },
                    { "7DA4779A-152E-44A2-A6F2-F80D991A5BEE", TraceEventLevel.Warning }, // DurableTask.Core
                };

                this.eventSourceListener.OnTraceLog += this.OnEventSourceListenerTraceLog;

                string sessionName = "DTFxTrace" + Guid.NewGuid().ToString("N");
                this.eventSourceListener.CaptureLogs(sessionName, traceConfig);
            }
        }

        private static bool IsLogFriendlyPlatform()
        {
            return !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator returns an OK (200) status code.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_SynchronousAPI_Returns200(string storageProvider)
        {
            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);
            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_SynchronousAPI_Returns200),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(400));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the UserAgent header is set in the HttpResponseMessage.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_CheckUserAgentHeader(string storageProvider)
        {
            HttpMessageHandler httpMessageHandler = CreateHttpMessageHandlerCheckUserAgent();

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_CheckUserAgentHeader),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(400));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator returns an Accepted (202)
        /// when the asynchronous pattern is disabled.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_AsynchronousPatternDisabled(string storageProvider)
        {
            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.Accepted);
            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousPatternDisabled),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler),
                asynchronousPatternEnabled: false))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(400));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator returns a Not Found (404) status code.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_SynchronousAPI_ReturnsNotFound(string storageProvider)
        {
            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.NotFound);
            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_SynchronousAPI_ReturnsNotFound),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(40));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator Headers and Content.
        /// from the response have relevant information. This test has multiple response
        /// header values.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_MultipleHeadersAndContentTest(string storageProvider)
        {
            string[] httpResponseHeaders = { "test.host.com", "test.response.com" };
            StringValues stringValues = new StringValues(httpResponseHeaders);
            Dictionary<string, StringValues> testHeaders = new Dictionary<string, StringValues>();
            testHeaders.Add("Host", stringValues);

            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessageMultHeaders(
                                                                                        statusCode: HttpStatusCode.OK,
                                                                                        headers: testHeaders,
                                                                                        content: "test content");

            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_MultipleHeadersAndContentTest),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                // Uri uri = new Uri("https://dummy-test-url.com");
                // var request = new DurableHttpRequest(HttpMethod.Get, uri);
                // StringValues stringValues = new StringValues("application/json");
                // request.Headers.Add("Accept", stringValues);

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");

                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                var client = await host.StartOrchestratorAsync(nameof(TestOrchestrations.CallHttpAsyncOrchestrator), testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));

                var output = status?.Output;

                JsonSerializer serializer = new JsonSerializer();
                serializer.Converters.Add(new DurableHttpResponseJsonConverter(typeof(DurableHttpResponse)));
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>(serializer);

                var hostHeaders = response.Headers["Host"];
                bool hasHostValueOne = response.Headers["Host"].Contains("test.host.com");
                bool hasHostValueTwo = response.Headers["Host"].Contains("test.response.com");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(hasHostValueOne && hasHostValueTwo);
                Assert.Contains("test content", response.Content);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator Headers and Content.
        /// from the response have relevant information. This test has multiple response
        /// headers with varying amount of header values.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_MultipleHeaderValuesTest(string storageProvider)
        {
            Dictionary<string, StringValues> testHeaders = new Dictionary<string, StringValues>();

            string[] httpResponseHeaders = { "test.host.com", "test.response.com" };
            StringValues stringValues = new StringValues(httpResponseHeaders);
            testHeaders.Add("Host", stringValues);

            string[] cacheResponseHeaders = { "GET", "POST", "HEAD", "OPTIONS" };
            StringValues cacheStringValues = new StringValues(cacheResponseHeaders);
            testHeaders.Add("Cache-Control", cacheStringValues);

            string[] accessControlHeaders = { "X-customHeader1", "X-customHeader2", "X-customHeader3", "X-customHeader4", "X-customHeader5" };
            StringValues accessControlStringValues = new StringValues(accessControlHeaders);
            testHeaders.Add("Access-Control-Expose-Headers", accessControlStringValues);

            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessageMultHeaders(
                                                                                        statusCode: HttpStatusCode.OK,
                                                                                        headers: testHeaders,
                                                                                        content: "test content");

            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_MultipleHeaderValuesTest),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");

                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                var client = await host.StartOrchestratorAsync(nameof(TestOrchestrations.CallHttpAsyncOrchestrator), testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));

                var output = status?.Output;

                JsonSerializer serializer = new JsonSerializer();
                serializer.Converters.Add(new DurableHttpResponseJsonConverter(typeof(DurableHttpResponse)));
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>(serializer);

                var hostHeaders = response.Headers["Host"];
                bool hasHostValueOne = response.Headers["Host"].Contains("test.host.com");
                bool hasHostValueTwo = response.Headers["Host"].Contains("test.response.com");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(hasHostValueOne && hasHostValueTwo);
                Assert.Contains("test content", response.Content);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator Headers and Content.
        /// from the response have relevant information. This test has one response header
        /// with one response header value.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_OneHeaderAndContentTest(string storageProvider)
        {
            string[] httpResponseHeaders = { "test.host.com" };
            StringValues stringValues = new StringValues(httpResponseHeaders);
            Dictionary<string, StringValues> testHeaders = new Dictionary<string, StringValues>();
            testHeaders.Add("Host", stringValues);

            HttpResponseMessage testHttpResponseMessage = CreateTestHttpResponseMessageMultHeaders(
                                                                                        statusCode: HttpStatusCode.OK,
                                                                                        headers: testHeaders,
                                                                                        content: "test content");

            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandler(testHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_OneHeaderAndContentTest),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                // Uri uri = new Uri("https://dummy-test-url.com");
                // var request = new DurableHttpRequest(HttpMethod.Get, uri);
                // StringValues stringValues = new StringValues("application/json");
                // request.Headers.Add("Accept", stringValues);

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");

                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                var client = await host.StartOrchestratorAsync(nameof(TestOrchestrations.CallHttpAsyncOrchestrator), testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));

                var output = status?.Output;

                JsonSerializer serializer = new JsonSerializer();
                serializer.Converters.Add(new DurableHttpResponseJsonConverter(typeof(DurableHttpResponse)));
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>(serializer);

                var hostHeaders = response.Headers["Host"];
                bool hasHostValueOne = response.Headers["Host"].Contains("test.host.com");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(hasHostValueOne);
                Assert.Contains("test content", response.Content);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator works with a
        /// Retry-After header.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_AsynchronousAPI_RetryAfterTest(string storageProvider)
        {
            Dictionary<string, string> testHeaders = new Dictionary<string, string>();
            testHeaders.Add("Retry-After", "20");
            testHeaders.Add("Location", "https://www.dummy-url.com");

            HttpResponseMessage acceptedHttpResponseMessage = CreateTestHttpResponseMessage(
                                                                                        statusCode: HttpStatusCode.Accepted,
                                                                                        headers: testHeaders);
            HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandlerWithRetryAfter(acceptedHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_RetryAfterTest),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(240));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator Async functionality
        /// waits until an OK response is returned.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_AsynchronousAPI_ReturnsOK200(string storageProvider)
        {
            Dictionary<string, string> asyncTestHeaders = new Dictionary<string, string>();
            asyncTestHeaders.Add("Location", "https://www.dummy-location-url.com");

            HttpResponseMessage acceptedHttpResponseMessage = CreateTestHttpResponseMessage(
                                                                                               statusCode: HttpStatusCode.Accepted,
                                                                                               headers: asyncTestHeaders);

            HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandler(acceptedHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_ReturnsOK200),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator Async functionality
        /// waits until an OK response is returned with a long running process.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_AsynchronousAPI_LongRunning(string storageProvider)
        {
            Dictionary<string, string> asyncTestHeaders = new Dictionary<string, string>();
            asyncTestHeaders.Add("Location", "https://www.dummy-location-url.com");

            HttpResponseMessage acceptedHttpResponseMessage = CreateTestHttpResponseMessage(
                                                                                               statusCode: HttpStatusCode.Accepted,
                                                                                               headers: asyncTestHeaders);
            HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandlerLongRunning(acceptedHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_LongRunning),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                httpAsyncSleepTime: 10000,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var client = await host.StartOrchestratorAsync(functionName, testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(40000));

                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if multiple CallHttpAsync Orchestrator Async calls
        /// all return an OK response status code.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_AsynchronousAPI_MultipleAsyncCalls(string storageProvider)
        {
            // HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandlerForMultipleRequests();

            Dictionary<string, string> asyncTestHeaders = new Dictionary<string, string>();
            asyncTestHeaders.Add("Location", "https://www.dummy-location-url.com");

            HttpResponseMessage acceptedHttpResponseMessage = CreateTestHttpResponseMessage(
                                                                                               statusCode: HttpStatusCode.Accepted,
                                                                                               headers: asyncTestHeaders);

            HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandler(acceptedHttpResponseMessage);

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_ReturnsOK200),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                // First request
                Dictionary<string, string> headersOne = new Dictionary<string, string>();
                headersOne.Add("Accept", "application/json");
                TestDurableHttpRequest testRequestOne = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headersOne);

                string functionNameOne = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var clientOne = await host.StartOrchestratorAsync(functionNameOne, testRequestOne, this.output);
                var statusOne = await clientOne.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));
                var outputOne = statusOne?.Output;
                DurableHttpResponse responseOne = outputOne.ToObject<DurableHttpResponse>();

                Assert.Equal(HttpStatusCode.OK, responseOne.StatusCode);

                // Second request
                Dictionary<string, string> headersTwo = new Dictionary<string, string>();
                headersTwo.Add("Accept", "application/json");
                TestDurableHttpRequest testRequestTwo = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headersTwo);

                string functionName = nameof(TestOrchestrations.CallHttpAsyncOrchestrator);
                var clientTwo = await host.StartOrchestratorAsync(functionName, testRequestTwo, this.output);
                var statusTwo = await clientTwo.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));
                var outputTwo = statusOne?.Output;
                DurableHttpResponse responseTwo = outputOne.ToObject<DurableHttpResponse>();

                Assert.Equal(HttpStatusCode.OK, responseTwo.StatusCode);

                await host.StopAsync();
            }
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator returns an OK (200) status code
        /// when a Bearer Token is added to the DurableHttpRequest object.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_Synchronous_AddsBearerToken(string storageProvider)
        {
            HttpMessageHandler httpMessageHandler = CreateSynchronousHttpMessageHandlerForTestingTokenSource();

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_ReturnsOK200),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                MockTokenSource mockTokenSource = new MockTokenSource("dummy test token");

                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers,
                    tokenSource: mockTokenSource);

                var client = await host.StartOrchestratorAsync(nameof(TestOrchestrations.CallHttpAsyncOrchestrator), testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));
                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        private static HttpMessageHandler CreateSynchronousHttpMessageHandlerForTestingTokenSource()
        {
            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);
            HttpResponseMessage forbiddenHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.Forbidden);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => HasBearerToken(req)), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(okHttpResponseMessage);

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => !HasBearerToken(req)), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(forbiddenHttpResponseMessage);

            return handlerMock.Object;
        }

        private static bool HasBearerToken(HttpRequestMessage req)
        {
            string headerValue = req.Headers.GetValues("Authorization").FirstOrDefault();
            return string.Equals(headerValue, "Bearer dummy test token");
        }

        /// <summary>
        /// End-to-end test which checks if the CallHttpAsync Orchestrator returns an OK (200) status code
        /// when a Bearer Token is added to the DurableHttpRequest object and follows the
        /// asynchronous pattern.
        /// </summary>
        [Theory]
        [Trait("Category", PlatformSpecificHelpers.TestCategory)]
        [MemberData(nameof(TestDataGenerator.GetFullFeaturedStorageProviderOptions), MemberType = typeof(TestDataGenerator))]
        public async Task DurableHttpAsync_Asynchronous_AddsBearerToken(string storageProvider)
        {
            HttpMessageHandler httpMessageHandler = CreateAsynchronousHttpMessageHandlerForTestingTokenSource();

            using (JobHost host = TestHelpers.GetJobHost(
                this.loggerProvider,
                nameof(this.DurableHttpAsync_AsynchronousAPI_ReturnsOK200),
                enableExtendedSessions: false,
                storageProviderType: storageProvider,
                durableHttpMessageHandler: new DurableHttpMessageHandlerFactory(httpMessageHandler)))
            {
                await host.StartAsync();

                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Accept", "application/json");
                MockTokenSource mockTokenSource = new MockTokenSource("dummy test token");

                TestDurableHttpRequest testRequest = new TestDurableHttpRequest(
                    httpMethod: HttpMethod.Get,
                    headers: headers,
                    tokenSource: mockTokenSource);

                var client = await host.StartOrchestratorAsync(nameof(TestOrchestrations.CallHttpAsyncOrchestrator), testRequest, this.output);
                var status = await client.WaitForCompletionAsync(this.output, timeout: TimeSpan.FromSeconds(Debugger.IsAttached ? 3000 : 90));
                var output = status?.Output;
                DurableHttpResponse response = output.ToObject<DurableHttpResponse>();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                await host.StopAsync();
            }
        }

        private static HttpMessageHandler CreateAsynchronousHttpMessageHandlerForTestingTokenSource()
        {
            Dictionary<string, string> asyncTestHeaders = new Dictionary<string, string>();
            asyncTestHeaders.Add("Location", "https://www.dummy-location-url.com");

            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);
            HttpResponseMessage forbiddenHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.Forbidden);
            HttpResponseMessage acceptedHttpResponseMessage = CreateTestHttpResponseMessage(
                                                                                               statusCode: HttpStatusCode.Accepted,
                                                                                               headers: asyncTestHeaders);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => HasBearerToken(req)), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    okHttpResponseMessage,
                }).Dequeue);

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => !HasBearerToken(req)), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    forbiddenHttpResponseMessage,
                }).Dequeue);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateAsynchronousHttpMessageHandlerForMultipleRequests()
        {
            Dictionary<string, string> asyncTestHeadersOne = new Dictionary<string, string>();
            asyncTestHeadersOne.Add("Location", "https://www.dummy-location-url.com/AsyncRequestOne");

            Dictionary<string, string> asyncTestHeadersTwo = new Dictionary<string, string>();
            asyncTestHeadersTwo.Add("Location", "https://www.dummy-location-url.com/AsyncRequestTwo");

            HttpResponseMessage acceptedHttpResponseMessageOne = CreateTestHttpResponseMessage(
                                                                                               statusCode: HttpStatusCode.Accepted,
                                                                                               headers: asyncTestHeadersOne);

            HttpResponseMessage acceptedHttpResponseMessageTwo = CreateTestHttpResponseMessage(
                                                                                              statusCode: HttpStatusCode.Accepted,
                                                                                              headers: asyncTestHeadersTwo);

            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().EndsWith("AsyncRequestOne")), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessageOne,
                    acceptedHttpResponseMessageOne,
                    acceptedHttpResponseMessageOne,
                    acceptedHttpResponseMessageOne,
                    okHttpResponseMessage,
                }).Dequeue);

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().EndsWith("AsyncRequestTwo")), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessageTwo,
                    acceptedHttpResponseMessageTwo,
                    acceptedHttpResponseMessageTwo,
                    acceptedHttpResponseMessageTwo,
                    okHttpResponseMessage,
                }).Dequeue);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateSynchronousHttpMessageHandler(HttpResponseMessage httpResponseMessage)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(httpResponseMessage);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateHttpMessageHandlerCheckUserAgent()
        {
            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);
            HttpResponseMessage forbiddenHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.Forbidden);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => req.Headers.UserAgent != null), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(okHttpResponseMessage);

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req => req.Headers.UserAgent == null), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(forbiddenHttpResponseMessage);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateAsynchronousHttpMessageHandler(HttpResponseMessage acceptedHttpResponseMessage)
        {
            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    okHttpResponseMessage,
                }).Dequeue);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateAsynchronousHttpMessageHandlerLongRunning(HttpResponseMessage acceptedHttpResponseMessage)
        {
            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    okHttpResponseMessage,
                }).Dequeue);

            return handlerMock.Object;
        }

        private static HttpMessageHandler CreateAsynchronousHttpMessageHandlerWithRetryAfter(HttpResponseMessage acceptedHttpResponseMessage)
        {
            HttpResponseMessage okHttpResponseMessage = CreateTestHttpResponseMessage(HttpStatusCode.OK);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new Queue<HttpResponseMessage>(new[]
                {
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    acceptedHttpResponseMessage,
                    okHttpResponseMessage,
                }).Dequeue);

            return handlerMock.Object;
        }

        private static HttpResponseMessage CreateTestHttpResponseMessage(
                                                                    HttpStatusCode statusCode,
                                                                    Dictionary<string, string> headers = null,
                                                                    string content = "")
        {
            HttpResponseMessage newHttpResponseMessage = new HttpResponseMessage(statusCode);
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    newHttpResponseMessage.Headers.Add(header.Key, header.Value);
                }
            }

            string json = JsonConvert.SerializeObject(content);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            newHttpResponseMessage.Content = httpContent;
            return newHttpResponseMessage;
        }

        private static HttpResponseMessage CreateTestHttpResponseMessageMultHeaders(
                                                                    HttpStatusCode statusCode,
                                                                    Dictionary<string, StringValues> headers = null,
                                                                    string content = "")
        {
            HttpResponseMessage newHttpResponseMessage = new HttpResponseMessage(statusCode);
            if (headers != null)
            {
                foreach (KeyValuePair<string, StringValues> header in headers)
                {
                    newHttpResponseMessage.Headers.Add(header.Key, (IEnumerable<string>)header.Value);
                }
            }

            string json = JsonConvert.SerializeObject(content);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            newHttpResponseMessage.Content = httpContent;
            return newHttpResponseMessage;
        }

        [DataContract]
        private class MockTokenSource : ITokenSource
        {
            [DataMember]
            private readonly string token;

            public MockTokenSource(string token)
            {
                this.token = token;
            }

            public Task<string> GetTokenAsync()
            {
                return Task.FromResult(this.token);
            }
        }
    }
}
