using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using ChoETL;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChoETL.Tests
{
    [TestFixture]
    public class JsonIssueTests
    {
        static string Normalize(string s) => s.Replace("\r\n", "\n");

        // #303: a JSON string value consisting of a single double-quote character
        [Test]
        public void Issue303_SingleQuoteCharValueDoesNotThrow()
        {
            string json = @"[{""id"": 1, ""messagestext"": ""\""""}, {""id"": 2, ""messagestext"": ""ok""}]";
            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(json)
                .Configure(c => c.FlattenNode = true)
                .Configure(c => c.NullValue = " ")
                .ErrorMode(ChoErrorMode.ThrowAndStop))
            using (var w = new ChoCSVWriter(sb).WithDelimiter(";").QuoteAllFields(true).WithFirstLineHeader())
            {
                w.Write(r);
            }
            var lines = Normalize(sb.ToString()).TrimEnd().Split('\n');
            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(lines[1], Is.EqualTo("\"1\";\"\"\"\""));
            Assert.That(lines[2], Is.EqualTo("\"2\";\"ok\""));
        }

        // #325: objects in an array with differently shaped "value" members
        [Test]
        public void Issue325_MixedShapeValuesAreNotDropped()
        {
            string json = @"{ ""extension"": [
                { ""Test"": ""value"", ""value"": [ ""0"", ""1"", ""2"" ] },
                { ""Test1"": ""Test"", ""value"": ""20"" },
                { ""Test2"": ""Test"", ""value"": ""35"" } ] }";
            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(json).WithJSONPath("$.extension").UseJsonSerialization())
            using (var w = new ChoCSVWriter(sb).WithFirstLineHeader().ThrowAndStopOnMissingField(false).WithMaxScanRows(3))
                w.Write(r);
            var lines = Normalize(sb.ToString()).TrimEnd().Split('\n');
            Assert.That(lines[0], Is.EqualTo("Test,value_0,value_1,value_2,Test1,Test2,value"));
            Assert.That(lines[1], Is.EqualTo("value,0,1,2,,,"));
            Assert.That(lines[2], Is.EqualTo(",,,,Test,,20"));
            Assert.That(lines[3], Is.EqualTo(",,,,,Test,35"));
        }

        // #106: keys that only appear in later records become columns when rows are scanned
        [Test]
        public void Issue106_LaterKeysBecomeColumns()
        {
            string json = @"[{""column_a"": 1,""column_b"": 2,""column_c"": 3},{""column_a"": 11,""column_x"": ""x"",""column_c"": 33}]";
            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(json).UseJsonSerialization())
            using (var w = new ChoCSVWriter(sb).WithFirstLineHeader().ThrowAndStopOnMissingField(false).WithMaxScanRows(2))
                w.Write(r);
            Assert.That(Normalize(sb.ToString()).TrimEnd(), Is.EqualTo("column_a,column_b,column_c,column_x\n1,2,3,\n11,,33,x"));
        }

        // #206: flatten an array of arrays
        [Test]
        public void Issue206_FlattenArrayOfArrays()
        {
            string json = @"{""id"": ""123456789"", ""TimeRanges"": [[0,24],[1,2]]}";
            var recs = ChoJSONReader.LoadText(json)
                .Configure(c => c.DefaultArrayHandling = false)
                .Configure(c => c.FlattenNode = true)
                .Configure(c => c.UseNestedKeyFormat = true)
                .Configure(c => c.FlattenByNodeName = "TimeRanges")
                .Configure(c => c.NestedKeySeparator = '.')
                .ToArray();
            Assert.That(recs.Length, Is.EqualTo(2));
            var r0 = (IDictionary<string, object>)recs[0];
            var r1 = (IDictionary<string, object>)recs[1];
            Assert.That(r0["id"], Is.EqualTo("123456789"));
            Assert.That(((System.Collections.IEnumerable)r0["TimeRange"]).Cast<object>().Select(Convert.ToInt32), Is.EqualTo(new[] { 0, 24 }));
            Assert.That(((System.Collections.IEnumerable)r1["TimeRange"]).Cast<object>().Select(Convert.ToInt32), Is.EqualTo(new[] { 1, 2 }));
        }

        // #304: an empty nested array must not break CSV output
        [Test]
        public void Issue304_EmptyNestedArrayToCsv()
        {
            string json = @"[
                { ""incident"": { ""name"": ""Fire1"" }, ""additionalClaimant"": [ { ""claimantFirstName"": ""Olga"" }, { ""claimantFirstName"": ""Meera"" } ] },
                { ""incident"": { ""name"": ""Fire2"" }, ""additionalClaimant"": [] } ]";
            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(json)
                .WithField("Fire", jsonPath: "$.incident.name", isArray: false)
                .WithField("FirstName", jsonPath: "$.additionalClaimant[*].claimantFirstName"))
            using (var w = new ChoCSVWriter(sb).WithFirstLineHeader().UseNestedKeyFormat(false)
                .Configure(c => c.ArrayValueSeparator = ';'))
            {
                w.Write(r);
            }
            var lines = Normalize(sb.ToString()).TrimEnd().Split('\n');
            Assert.That(lines[0], Is.EqualTo("Fire,FirstName"));
            Assert.That(lines[1], Does.StartWith("Fire1,"));
            Assert.That(lines[1], Does.Contain("Olga").And.Contain("Meera"));
            Assert.That(lines[2], Is.EqualTo("Fire2,"));
        }

        // Same defect class as #322: Write(DataTable) recursed forever with UseJSONSerialization
        [Test]
        public void Issue322_JsonWriteDataTableWithJsonSerializationDoesNotRecurse()
        {
            var dt = new DataTable("Emp");
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("Name", typeof(string));
            dt.Rows.Add(1, "Tom");
            var sb = new StringBuilder();
            using (var w = new ChoJSONWriter(sb).UseJsonSerialization())
                w.Write(dt);
            //DataTables are written as { "<TableName>": [ rows ] }
            var arr = (JArray)JObject.Parse(sb.ToString())["Emp"];
            Assert.That((int)arr[0]["Id"], Is.EqualTo(1));
            Assert.That((string)arr[0]["Name"], Is.EqualTo("Tom"));
        }
    }

    [TestFixture]
    public class XmlIssueTests
    {
        // #173: default xmlns on the root element
        [XmlRoot("Project")]
        public class MsbuildProject
        {
            [XmlAttribute] public string ToolsVersion { get; set; }
            [XmlElement("PropertyGroup")] public List<PropertyGroup> PropertyGroup { get; set; }
        }

        public class PropertyGroup
        {
            public string OutputType { get; set; }
            public string AssemblyName { get; set; }
        }

        [Test]
        public void Issue173_DefaultNamespaceIsHandled()
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>Demo</AssemblyName>
  </PropertyGroup>
</Project>";
            var recs = ChoXmlReader<MsbuildProject>.LoadText(xml).WithXPath("/").ToArray();
            Assert.That(recs.Length, Is.EqualTo(1));
            Assert.That(recs[0].ToolsVersion, Is.EqualTo("15.0"));
            Assert.That(recs[0].PropertyGroup?.FirstOrDefault()?.AssemblyName, Is.EqualTo("Demo"));
        }

        // #203 / #177: repeated (unwrapped) child elements bound to a List<T>.
        // POCO lists are deserialized with XmlSerializer semantics, so an unwrapped list needs [XmlElement]
        // (without it XmlSerializer expects <PackageReference><PackageReference/>...</PackageReference>).
        public class SdkProject
        {
            [XmlElement] public List<ItemGroup> ItemGroup { get; set; }
        }

        public class ItemGroup
        {
            [XmlAttribute] public string Label { get; set; }
            [XmlElement] public List<PackageReference> PackageReference { get; set; }
        }

        public class PackageReference
        {
            [XmlAttribute] public string Include { get; set; }
            [XmlAttribute] public string Version { get; set; }
        }

        [Test]
        public void Issue203_NestedListWithoutXmlElementAttribute()
        {
            string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.0.0"" />
    <PackageReference Include=""MSTest.TestAdapter"" Version=""2.2.8"" />
  </ItemGroup>
</Project>";
            var rec = ChoXmlReader<SdkProject>.LoadText(xml).WithXPath("/").First();
            var refs = rec.ItemGroup.SelectMany(g => g.PackageReference ?? new List<PackageReference>()).ToArray();
            Assert.That(refs.Select(p => p.Include), Is.EqualTo(new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter" }));
            Assert.That(refs.Select(p => p.Version), Is.EqualTo(new[] { "17.0.0", "2.2.8" }));
        }

        // #316: configurable element name for items of JSON arrays
        [Test]
        public void Issue316_ArrayItemNodeName()
        {
            var json = @"[ { ""name"": ""John"", ""phones"": [ { ""type"": ""Home"", ""number"": ""1"" }, { ""type"": ""Work"", ""number"": ""2"" } ] } ]";
            var sb = new StringBuilder();
            using (var xml = new ChoXmlWriter(sb).Configure(x =>
            {
                x.RootName = "root";
                x.NodeName = "item";
                x.UseXmlArray = true;
                x.DoNotEmitXmlNamespace = true;
                x.ThrowAndStopOnMissingField = false;
                x.ArrayItemNodeName = "item";
            }))
            using (var r = new ChoJSONReader(JToken.Parse(json)))
            {
                xml.Write(r);
            }
            var doc = XDocument.Parse(sb.ToString());
            var phones = doc.Descendants("phones").Single();
            Assert.That(phones.Elements().Select(e => e.Name.LocalName), Is.EqualTo(new[] { "item", "item" }));
            Assert.That(phones.Elements().First().Element("type")?.Value, Is.EqualTo("Home"));
        }

        // #131: repeated LoadXElement calls must not keep generating XmlSerializer assemblies
        public class Entity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<string> Tags { get; set; }
        }

        [Test]
        public void Issue131_RepeatedLoadXElementDoesNotLeakAssemblies()
        {
            var element = XElement.Parse("<Entity><Id>1</Id><Name>n</Name><Tags><string>a</string></Tags></Entity>");
            for (int i = 0; i < 50; i++)
                ChoXmlReader<Entity>.LoadXElement(element);

            int before = AppDomain.CurrentDomain.GetAssemblies().Length;
            Entity last = null;
            for (int i = 0; i < 2000; i++)
                last = ChoXmlReader<Entity>.LoadXElement(element);
            int after = AppDomain.CurrentDomain.GetAssemblies().Length;

            Assert.That(last.Name, Is.EqualTo("n"));
            Assert.That(after - before, Is.LessThanOrEqualTo(1), "XmlSerializer assemblies are being generated per call");
        }

        // Same defect class as #322: Write(DataTable) recursed forever with UseXmlSerialization
        [Test]
        public void Issue322_XmlWriteDataTableWithXmlSerializationDoesNotRecurse()
        {
            var dt = new DataTable("Emp");
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("Name", typeof(string));
            dt.Rows.Add(1, "Tom");
            var sb = new StringBuilder();
            using (var w = new ChoXmlWriter(sb).Configure(c => c.UseXmlSerialization = true))
                w.Write(dt);
            Assert.That(sb.ToString(), Does.Contain("Tom"));
        }
    }

    [TestFixture]
    public class YamlIssueTests
    {
        // #322: Write(IDataReader) used to recurse forever
        [Test]
        public void Issue322_YamlWriteDataReader()
        {
            var dt = new DataTable("Emp");
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("Name", typeof(string));
            dt.Rows.Add(1, "Tom");
            dt.Rows.Add(2, "Mark");
            var sb = new StringBuilder();
            using (var w = new ChoYamlWriter(sb).Configure(c => c.UseYamlSerialization = true))
                w.Write(dt.CreateDataReader());
            var yaml = sb.ToString();
            Assert.That(yaml, Does.Contain("Tom"));
            Assert.That(yaml, Does.Contain("Mark"));
        }
    }
}
