using System.Collections.Generic;
using FastTests;
using Xunit;

namespace SlowTests.Bugs
{
    public class DeserializationAcrossTypes : RavenNewTestBase
    {
        [Fact]
        public void can_deserialize_across_types_when_origin_type_doesnt_exist()
        {
            using (var store = GetDocumentStore())
            {
                store.Commands().Put("alphas/1", null, new { Foo =  "Bar"}, new Dictionary<string, string>
                {
                    { "@collection", "Alphas" },
                    { "Raven-Clr-Type", "SlowTests.Bugs.Second.Alpha" }
                });

                using (var session = store.OpenSession())
                {
                    session.Load<Alpha>("alphas/1");
                }
            }
        }

        private class Alpha
        {
            public string Foo { get; set; }
        }
    }
}