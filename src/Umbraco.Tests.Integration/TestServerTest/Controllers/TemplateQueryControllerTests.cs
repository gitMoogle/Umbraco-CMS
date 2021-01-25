// Copyright (c) Umbraco.
// See LICENSE for more details.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Umbraco.Core;
using Umbraco.Tests.Common.Builders.Extensions;
using Umbraco.Tests.Testing;
using Umbraco.Web.BackOffice.Controllers;
using Umbraco.Web.Common.Formatters;
using Umbraco.Web.Models.TemplateQuery;

namespace Umbraco.Tests.Integration.TestServerTest.Controllers
{
    [TestFixture]
    public class TemplateQueryControllerTests : UmbracoTestServerTestBase
    {
        [Test]
        public async Task GetContentTypes__Ensure_camel_case()
        {
            string url = PrepareUrl<TemplateQueryController>(x => x.GetContentTypes());

            // Act
            HttpResponseMessage response = await Client.GetAsync(url);

            string body = await response.Content.ReadAsStringAsync();

            body = body.TrimStart(AngularJsonMediaTypeFormatter.XsrfPrefix);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

                Assert.DoesNotThrow(() => JsonConvert.DeserializeObject<ContentTypeModel[]>(body));

                JToken[] jtokens = JsonConvert.DeserializeObject<JToken[]>(body);
                foreach (JToken jToken in jtokens)
                {
                    string alias = nameof(ContentTypeModel.Alias);
                    string camelCaseAlias = alias.ToCamelCase();
                    Assert.IsNotNull(jToken.Value<string>(camelCaseAlias), $"'{jToken}' do not contain the key '{camelCaseAlias}' in the expected casing");
                    Assert.IsNull(jToken.Value<string>(alias), $"'{jToken}' do contain the key '{alias}', which was not expect in that casing");
                }
            });
        }
    }
}