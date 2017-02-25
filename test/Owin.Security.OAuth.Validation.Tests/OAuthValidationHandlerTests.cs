﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Extensions for more information
 * concerning the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Owin.Security;
using Microsoft.Owin.Testing;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Owin.Security.OAuth.Validation.Tests
{
    public class OAuthValidationHandlerTests
    {
        [Fact]
        public async Task AuthenticateCoreAsync_InvalidTokenCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer();
            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ValidTokenAllowsSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer();
            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_MissingAudienceCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Audiences.Add("http://www.fabrikam.com/");
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_InvalidAudienceCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Audiences.Add("http://www.fabrikam.com/");
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-with-single-audience");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ValidAudienceAllowsSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Audiences.Add("http://www.fabrikam.com/");
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-with-multiple-audiences");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_AnyMatchingAudienceCausesSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Audiences.Add("http://www.contoso.com/");
                options.Audiences.Add("http://www.fabrikam.com/");
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-with-single-audience");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_MultipleMatchingAudienceCausesSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Audiences.Add("http://www.contoso.com/");
                options.Audiences.Add("http://www.fabrikam.com/");
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-with-multiple-audiences");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ExpiredTicketCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer();

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "expired-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_AuthenticationTicketContainsRequiredClaims()
        {
            // Arrange
            var server = CreateResourceServer();

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/ticket");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-with-scopes");

            // Act
            var response = await client.SendAsync(request);

            var ticket = JObject.Parse(await response.Content.ReadAsStringAsync());
            var claims = from claim in ticket.Value<JArray>("Claims")
                         select new
                         {
                             Type = claim.Value<string>(nameof(Claim.Type)),
                             Value = claim.Value<string>(nameof(Claim.Value))
                         };

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Contains(claims, claim => claim.Type == OAuthValidationConstants.Claims.Subject &&
                                             claim.Value == "Fabrikam");

            Assert.Contains(claims, claim => claim.Type == OAuthValidationConstants.Claims.Scope &&
                                             claim.Value == "C54A8F5E-0387-43F4-BA43-FD4B50DC190D");

            Assert.Contains(claims, claim => claim.Type == OAuthValidationConstants.Claims.Scope &&
                                             claim.Value == "5C57E3BD-9EFB-4224-9AB8-C8C5E009FFD7");
        }

        [Fact]
        public async Task AuthenticateCoreAsync_AuthenticationTicketContainsRequiredProperties()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.SaveToken = true;
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/ticket");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            var ticket = JObject.Parse(await response.Content.ReadAsStringAsync());
            var properties = from claim in ticket.Value<JArray>("Properties")
                             select new
                             {
                                 Name = claim.Value<string>("Name"),
                                 Value = claim.Value<string>("Value")
                             };

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Contains(properties, property => property.Name == "access_token" &&
                                                    property.Value == "valid-token");
        }

        [Fact]
        public async Task AuthenticateCoreAsync_InvalidReplacedTokenCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnRetrieveToken = context =>
                {
                    context.Token = "invalid-token";

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ValidReplacedTokenCausesSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnRetrieveToken = context =>
                {
                    context.Token = "valid-token";

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_SkipToNextMiddlewareFromReceiveTokenCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnRetrieveToken = context =>
                {
                    context.SkipToNextMiddleware();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_NullTicketAndHandleResponseFromReceiveTokenCauseInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnRetrieveToken = context =>
                {
                    context.Ticket = null;
                    context.HandleResponse();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ReplacedTicketAndHandleResponseFromReceiveTokenCauseSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnRetrieveToken = context =>
                {
                    var identity = new ClaimsIdentity(context.Options.AuthenticationType);
                    identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                    context.Ticket = new AuthenticationTicket(identity, new AuthenticationProperties());

                    context.HandleResponse();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Fabrikam", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_SkipToNextMiddlewareFromValidateTokenCausesInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnValidateToken = context =>
                {
                    context.SkipToNextMiddleware();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_NullTicketAndHandleResponseFromValidateTokenCauseInvalidAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnValidateToken = context =>
                {
                    context.Ticket = null;
                    context.HandleResponse();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticateCoreAsync_ReplacedTicketAndHandleResponseFromValidateTokenCauseSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnValidateToken = context =>
                {
                    var identity = new ClaimsIdentity(context.Options.AuthenticationType);
                    identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Contoso"));

                    context.Ticket = new AuthenticationTicket(identity, new AuthenticationProperties());
                    context.HandleResponse();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Contoso", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AuthenticateCoreAsync_UpdatedTicketFromValidateTokenCausesSuccessfulAuthentication()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnValidateToken = context =>
                {
                    var identity = new ClaimsIdentity(context.Options.AuthenticationType);
                    identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Contoso"));

                    context.Ticket = new AuthenticationTicket(identity, new AuthenticationProperties());
                    context.HandleResponse();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Contoso", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task HandleUnauthorizedAsync_ErrorDetailsAreResolvedFromChallengeContext()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.IncludeErrorDetails = false;
                options.Realm = "global_realm";

                options.Events.OnApplyChallenge = context =>
                {
                    // Assert
                    Assert.Equal(context.Error, "custom_error");
                    Assert.Equal(context.ErrorDescription, "custom_error_description");
                    Assert.Equal(context.ErrorUri, "custom_error_uri");
                    Assert.Equal(context.Realm, "custom_realm");
                    Assert.Equal(context.Scope, "custom_scope");

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            // Act
            var response = await client.GetAsync("/challenge");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(@"Bearer realm=""custom_realm"", error=""custom_error"", error_description=""custom_error_description"", " +
                         @"error_uri=""custom_error_uri"", scope=""custom_scope""", response.Headers.WwwAuthenticate.ToString());
        }

        [Theory]
        [InlineData("invalid-token", OAuthValidationConstants.Errors.InvalidToken, "The access token is not valid.")]
        [InlineData("expired-token", OAuthValidationConstants.Errors.InvalidToken, "The access token is expired.")]
        public async Task HandleUnauthorizedAsync_ErrorDetailsAreInferredFromAuthenticationFailure(
            string token, string error, string description)
        {
            // Arrange
            var server = CreateResourceServer();
            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal($@"Bearer error=""{error}"", error_description=""{description}""",
                         response.Headers.WwwAuthenticate.ToString());
        }

        [Fact]
        public async Task HandleUnauthorizedAsync_ApplyChallenge_AllowsHandlingResponse()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnApplyChallenge = context =>
                {
                    context.HandleResponse();
                    context.OwinContext.Response.Headers["X-Custom-Authentication-Header"] = "Bearer";

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            // Act
            var response = await client.GetAsync("/challenge");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Equal(new[] { "Bearer" }, response.Headers.GetValues("X-Custom-Authentication-Header"));
        }

        [Fact]
        public async Task HandleUnauthorizedAsync_ApplyChallenge_AllowsSkippingToNextMiddleware()
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnApplyChallenge = context =>
                {
                    context.SkipToNextMiddleware();

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            // Act
            var response = await client.GetAsync("/challenge");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData(null, null, null, null, null, "Bearer")]
        [InlineData("custom_error", null, null, null, null, @"Bearer error=""custom_error""")]
        [InlineData(null, "custom_error_description", null, null, null, @"Bearer error_description=""custom_error_description""")]
        [InlineData(null, null, "custom_error_uri", null, null, @"Bearer error_uri=""custom_error_uri""")]
        [InlineData(null, null, null, "custom_realm", null, @"Bearer realm=""custom_realm""")]
        [InlineData(null, null, null, null, "custom_scope", @"Bearer scope=""custom_scope""")]
        [InlineData("custom_error", "custom_error_description", "custom_error_uri", "custom_realm", "custom_scope",
                    @"Bearer realm=""custom_realm"", error=""custom_error"", " +
                    @"error_description=""custom_error_description"", " +
                    @"error_uri=""custom_error_uri"", scope=""custom_scope""")]
        public async Task HandleUnauthorizedAsync_ReturnsExpectedWwwAuthenticateHeader(
            string error, string description, string uri, string realm, string scope, string header)
        {
            // Arrange
            var server = CreateResourceServer(options =>
            {
                options.Events.OnApplyChallenge = context =>
                {
                    context.Error = error;
                    context.ErrorDescription = description;
                    context.ErrorUri = uri;
                    context.Realm = realm;
                    context.Scope = scope;

                    return Task.FromResult(0);
                };
            });

            var client = server.HttpClient;

            // Act
            var response = await client.GetAsync("/challenge");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(header, response.Headers.WwwAuthenticate.ToString());
        }

        private static TestServer CreateResourceServer(Action<OAuthValidationOptions> configuration = null)
        {
            var format = new Mock<ISecureDataFormat<AuthenticationTicket>>(MockBehavior.Strict);

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "invalid-token")))
                  .Returns(value: null);

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "valid-token")))
                  .Returns(delegate
                  {
                      var identity = new ClaimsIdentity(OAuthValidationDefaults.AuthenticationScheme);
                      identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                      return new AuthenticationTicket(identity, new AuthenticationProperties());
                  });

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "valid-token-with-scopes")))
                  .Returns(delegate
                  {
                      var identity = new ClaimsIdentity(OAuthValidationDefaults.AuthenticationScheme);
                      identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                      var properties = new AuthenticationProperties();
                      properties.Dictionary[OAuthValidationConstants.Properties.Scopes] =
                        @"[""C54A8F5E-0387-43F4-BA43-FD4B50DC190D"",""5C57E3BD-9EFB-4224-9AB8-C8C5E009FFD7""]";

                      return new AuthenticationTicket(identity, properties);
                  });

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "valid-token-with-single-audience")))
                  .Returns(delegate
                  {
                      var identity = new ClaimsIdentity(OAuthValidationDefaults.AuthenticationScheme);
                      identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                      var properties = new AuthenticationProperties(new Dictionary<string, string>
                      {
                          [OAuthValidationConstants.Properties.Audiences] = @"[""http://www.contoso.com/""]"
                      });

                      return new AuthenticationTicket(identity, properties);
                  });

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "valid-token-with-multiple-audiences")))
                  .Returns(delegate
                  {
                      var identity = new ClaimsIdentity(OAuthValidationDefaults.AuthenticationScheme);
                      identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                      var properties = new AuthenticationProperties(new Dictionary<string, string>
                      {
                          [OAuthValidationConstants.Properties.Audiences] = @"[""http://www.contoso.com/"",""http://www.fabrikam.com/""]"
                      });

                      return new AuthenticationTicket(identity, properties);
                  });

            format.Setup(mock => mock.Unprotect(It.Is<string>(token => token == "expired-token")))
                  .Returns(delegate
                  {
                      var identity = new ClaimsIdentity(OAuthValidationDefaults.AuthenticationScheme);
                      identity.AddClaim(new Claim(OAuthValidationConstants.Claims.Subject, "Fabrikam"));

                      var properties = new AuthenticationProperties();
                      properties.ExpiresUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);

                      return new AuthenticationTicket(identity, properties);
                  });

            return TestServer.Create(app =>
            {
                app.UseOAuthValidation(options =>
                {
                    options.AuthenticationMode = AuthenticationMode.Active;
                    options.AccessTokenFormat = format.Object;

                    // Note: overriding the default data protection provider is not necessary for the tests to pass,
                    // but is useful to ensure unnecessary keys are not persisted in testing environments, which also
                    // helps make the unit tests run faster, as no registry or disk access is required in this case.
                    options.DataProtectionProvider = new EphemeralDataProtectionProvider();

                    // Run the configuration delegate
                    // registered by the unit tests.
                    configuration?.Invoke(options);
                });

                app.Map("/ticket", map => map.Run(async context =>
                {
                    var ticket = await context.Authentication.AuthenticateAsync(OAuthValidationDefaults.AuthenticationScheme);
                    if (ticket == null)
                    {
                        context.Authentication.Challenge();

                        return;
                    }

                    context.Response.ContentType = "application/json";

                    // Return the authentication ticket as a JSON object.
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(new
                    {
                        Claims = from claim in ticket.Identity.Claims
                                 select new { claim.Type, claim.Value },

                        Properties = from property in ticket.Properties.Dictionary
                                     select new { Name = property.Key, property.Value }
                    }));
                }));

                app.Map("/challenge", map => map.Run(context =>
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OAuthValidationConstants.Properties.Error] = "custom_error",
                        [OAuthValidationConstants.Properties.ErrorDescription] = "custom_error_description",
                        [OAuthValidationConstants.Properties.ErrorUri] = "custom_error_uri",
                        [OAuthValidationConstants.Properties.Realm] = "custom_realm",
                        [OAuthValidationConstants.Properties.Scope] = "custom_scope",
                    });

                    context.Authentication.Challenge(properties, OAuthValidationDefaults.AuthenticationScheme);

                    return Task.FromResult(0);
                }));

                app.Run(context =>
                {
                    if (context.Authentication.User == null ||
                       !context.Authentication.User.Identities.Any(identity => identity.IsAuthenticated))
                    {
                        context.Authentication.Challenge();

                        return Task.FromResult(0);
                    }

                    var identifier = context.Authentication.User.FindFirst(OAuthValidationConstants.Claims.Subject).Value;
                    return context.Response.WriteAsync(identifier);
                });
            });
        }
    }
}
