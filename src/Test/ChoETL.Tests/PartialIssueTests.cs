using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ChoETL;
using NUnit.Framework;

namespace ChoETL.Tests
{
    /// <summary>
    /// Regression tests built from the reporters' own attachments / descriptions for the issues that were
    /// only partly addressed in the first pass (#303, #325, #293, #256, #191, and the MaxScanRows data loss
    /// found through the patch attached to #206).
    /// </summary>
    [TestFixture]
    public class PartialIssueTests
    {
        static string Normalize(string s) => s.Replace("\r\n", "\n");

        static string DataFile(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", name);

        // #303: the reporter's file is a Telegram chat export where "text" is either a string or an array of
        // strings and entity objects; one array element is a lone double quote. (Synthetic data of the same
        // shape: the original contains third-party personal data.) Reporter's configuration, translated from VB.
        [Test]
        public void Issue303_ChatExportWithLoneQuoteTextElement()
        {
            string json = @"{
              ""name"": ""chat"", ""type"": ""public_supergroup"", ""id"": 1,
              ""messages"": [
                { ""id"": 1, ""type"": ""service"", ""date"": ""2023-01-01T10:00:00"", ""actor"": ""a"", ""action"": ""create_group"", ""title"": ""t"", ""text"": """" },
                { ""id"": 2, ""type"": ""message"", ""date"": ""2023-01-01T10:01:00"", ""from"": ""u"", ""text"": ""plain text"" },
                { ""id"": 3, ""type"": ""message"", ""date"": ""2023-01-01T10:02:00"", ""from"": ""u"",
                  ""text"": [ ""quote: \"""", { ""type"": ""mention"", ""text"": ""@someone"" }, ""\"""" ] }
              ] }";

            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(json))
            {
                r.Configuration.FlattenNode = true;
                r.Configuration.NullValue = " ";
                r.ErrorMode(ChoErrorMode.ThrowAndStop);
                using (var w = new ChoCSVWriter(new StringWriter(sb)))
                {
                    w.WithDelimiter(";");
                    w.QuoteAllFields(true);
                    w.WithFirstLineHeader();
                    w.Write(r);
                }
            }

            var lines = Normalize(sb.ToString()).TrimEnd().Split('\n');
            Assert.That(lines.Length, Is.GreaterThan(3));
            Assert.That(lines.Any(l => l.EndsWith(";\"\"\"\"")), Is.True, "the lone quote element must be written as \"\"\"\"");
        }

        // #325: the reporter's Sample.json (two of its four entries). The same member is a scalar in some
        // "testtension" items and an array in others, and the items are in a different order per entry.
        // Every value must survive the conversion (upstream dropped one value in every row).
        const string Sample325 = @"{
  ""Name"": ""Test"", ""type"": ""Test"",
  ""entry"": [
    { ""testUrl"": ""https://"",
      ""resource"": { ""resourceType"": ""test"", ""managingtest"": { ""test"": ""test/1807"" },
        ""name"": [ { ""firstname"": ""john"", ""lastname"": ""smith"", ""given"": [ ""Robet"" ] } ],
        ""gender"": ""male"", ""birthDate"": ""1925-01-01"",
        ""testtension"": [
          { ""url"": ""www.com"", ""value"": [ ""test1"", ""test2"", ""test3"" ] },
          { ""url"": ""www.1.com"", ""value"": ""test20"" },
          { ""url"": ""www.2.com"", ""value"": ""test30"" },
          { ""url"": ""www.3.com"", ""value"": ""test40"" } ],
        ""sata"": { ""YearUpdated"": ""2024"" } } },
    { ""testUrl"": ""https://"",
      ""resource"": { ""resourceType"": ""test"", ""managingtest"": { ""test"": ""test/1808"" },
        ""name"": [ { ""firstname"": ""hello"", ""lastname"": ""smith"", ""given"": [ ""test"" ] } ],
        ""gender"": ""male"", ""birthDate"": ""1925-01-01"",
        ""testtension"": [
          { ""url"": ""www.1.com"", ""value"": ""test20"" },
          { ""url"": ""www.2.com"", ""value"": ""test30"" },
          { ""url"": ""www.com"", ""value"": [ ""test1"", ""test2"", ""test3"" ] },
          { ""url"": ""www.3.com"", ""value"": ""test40"" } ],
        ""sata"": { ""YearUpdated"": ""2024"" } } }
  ] }";

        [Test]
        public void Issue325_ReporterSampleKeepsEveryValueInEveryRow()
        {
            var sb = new StringBuilder();
            using (var r = ChoJSONReader.LoadText(Sample325).WithJSONPath("$.entry").Configure(c => c.FlattenNode = true))
            using (var w = new ChoCSVWriter(sb).WithFirstLineHeader().ThrowAndStopOnMissingField(false).WithMaxScanRows(100))
                w.Write(r);

            var rows = Normalize(sb.ToString()).TrimEnd().Split('\n').Skip(1).ToArray();
            Assert.That(rows.Length, Is.EqualTo(2));
            foreach (var row in rows)
            {
                var cells = row.Split(',');
                Assert.That(new[] { "test1", "test2", "test3", "test20", "test30", "test40" }, Is.SubsetOf(cells), row);
            }
        }

        // Found through the patch attached to #206: the JSON reader enumerated its forward-only source twice
        // when MaxScanRows was set (field scan + main loop) and read one record past the scan limit,
        // silently dropping records (6 records, MaxScanRows = 1 -> only ids 1, 2, 4 came back).
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(10)]
        public void JsonReaderMaxScanRowsDoesNotDropRecords(int maxScanRows)
        {
            string json = "[" + string.Join(",", Enumerable.Range(1, 6).Select(i => $"{{\"id\":{i},\"v\":\"x{i}\"}}")) + "]";

            using (var r = ChoJSONReader.LoadText(json).Configure(c => c.MaxScanRows = maxScanRows))
                Assert.That(r.Select(x => (long)x.id), Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 6 }));

            using (var r = new ChoJSONReader(new MemoryStream(Encoding.UTF8.GetBytes(json))).Configure(c => c.MaxScanRows = maxScanRows))
                Assert.That(r.Select(x => (long)x.id), Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void JsonReaderMaxScanRowsWithFlattenNodeKeepsAllFlattenedRows()
        {
            // each source object flattens into two rows; the scan must not stop in the middle of one
            string json = "[" + string.Join(",", Enumerable.Range(1, 4).Select(i => $"{{\"id\":{i},\"items\":[{{\"n\":\"a{i}\"}},{{\"n\":\"b{i}\"}}]}}")) + "]";
            using (var r = ChoJSONReader.LoadText(json).Configure(c => c.FlattenNode = true).Configure(c => c.MaxScanRows = 1))
            {
                var rows = r.Cast<IDictionary<string, object>>().ToArray();
                Assert.That(rows.Length, Is.EqualTo(8));
                Assert.That(rows.Select(x => Convert.ToInt64(x["id"])), Is.EqualTo(new long[] { 1, 1, 2, 2, 3, 3, 4, 4 }));
            }
        }

        // #293: IgnoreDictionaryFieldPrefix makes nested members collide with root members (root 'id' and
        // 'owner.id'); that used to fail with "An item with the same key has already been added".
        // Reporter's configuration (JSON reader + parquet writer settings).
        [Test]
        public void Issue293_CollidingKeysWithIgnoreDictionaryFieldPrefix()
        {
            string json = @"[{""id"":1,""created"":""2023-08-01T10:00:00Z"",""owner"":{""id"":7,""name"":""a""}},
                             {""id"":2,""created"":""2023-08-02T10:00:00Z"",""owner"":{""id"":8,""name"":""b""}}]";
            var ms = new MemoryStream();
            using (var r = new ChoJSONReader(new StringReader(json), new ChoJSONRecordConfiguration
            {
                ThrowAndStopOnMissingField = false,
                FlattenNode = true,
                TypeConverterFormatSpec = new ChoTypeConverterFormatSpec { DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ" },
            }))
            using (var w = new ChoParquetWriter(ms, new ChoParquetRecordConfiguration
            {
                TreatDateTimeAsDateTimeOffset = true,
                IgnoreDictionaryFieldPrefix = true,
                UseNestedKeyFormat = true,
                LeaveStreamOpen = true,
            }))
            {
                w.Write(r);
            }

            using var pr = new ChoParquetReader(new MemoryStream(ms.ToArray()));
            var recs = pr.Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Length, Is.EqualTo(2));
            Assert.That(recs[0].Keys, Is.EqualTo(new[] { "id", "created", "owner/id", "name" }));
            Assert.That(Convert.ToInt64(recs[0]["id"]), Is.EqualTo(1));
            Assert.That(Convert.ToInt64(recs[0]["owner/id"]), Is.EqualTo(7));
            Assert.That(recs[1]["name"], Is.EqualTo("b"));
        }

        [Test]
        public void FlattenWithIgnoredPrefixesProducesUniqueKeys()
        {
            var dict = new Dictionary<string, object>
            {
                ["id"] = 1,
                ["a"] = new Dictionary<string, object> { ["id"] = 2, ["x"] = "ax" },
                ["b"] = new Dictionary<string, object> { ["id"] = 3 },
            };
            var keys = dict.Flatten('_', null, null, true).Select(kvp => kvp.Key).ToArray();
            Assert.That(keys, Is.EqualTo(new[] { "id", "a_id", "x", "b_id" }));
        }

        // #256: Parquet files written by Python (pandas/pyarrow). Generated by Data/generate_pandas_dates.py:
        //   df = DataFrame({"id": range(27), "date": date_range('2022-11-05', '2022-12-01', freq='d')})
        // Legacy INT96 timestamps crashed with "Cannot use type 'System.Numerics.BigInteger'" unless
        // TreatBigIntegersAsDates was on; they are now always read as dates.
        [TestCase("pandas_dates_int96.parquet", false)]
        [TestCase("pandas_dates_int96.parquet", true)]
        [TestCase("pandas_dates_us_and_date32.parquet", false)]
        [TestCase("pandas_dates_us_and_date32.parquet", true)]
        public void Issue256_PandasWrittenDatesAreReadAsDateTime(string file, bool treatBigIntegersAsDates)
        {
            using (var r = new ChoParquetReader(DataFile(file), new ChoParquetRecordConfiguration { ParquetOptions = { TreatBigIntegersAsDates = treatBigIntegersAsDates } }))
            {
                var recs = r.Cast<IDictionary<string, object>>().ToArray();
                Assert.That(recs.Length, Is.EqualTo(27));
                Assert.That(recs[0]["date"], Is.EqualTo(new DateTime(2022, 11, 5)));
                Assert.That(recs[26]["date"], Is.EqualTo(new DateTime(2022, 12, 1)));
                if (recs[0].ContainsKey("day"))
                    Assert.That(recs[0]["day"], Is.EqualTo(new DateTime(2022, 11, 5)));
            }

            // the reporter's call
            using (var r = new ChoParquetReader(DataFile(file), new ChoParquetRecordConfiguration { ParquetOptions = { TreatBigIntegersAsDates = treatBigIntegersAsDates } }))
            {
                DataTable dt = r.AsDataTable();
                Assert.That(dt.Rows.Count, Is.EqualTo(27));
                Assert.That(dt.Columns["date"].DataType, Is.EqualTo(typeof(DateTime)));
                Assert.That(dt.Rows[0]["date"], Is.EqualTo(new DateTime(2022, 11, 5)));
            }
        }

        // #191 (Apr 25 2022 comment): flatten an array of objects that have their own arrays.
        // FlattenByJsonPath dropped the root fields (BrandId); FlattenByNodeName kept them.
        const string Json191 = @"{
          ""BrandId"": ""B1"",
          ""Contact"": [
            { ""Value"": ""c1"", ""PostCode"": [ { ""Area"": ""a1"", ""Value"": ""p1"" }, { ""Area"": ""a2"", ""Value"": ""p2"" } ], ""Description.vi"": ""d1"", ""Type"": ""t1"" },
            { ""Value"": ""c2"", ""PostCode"": [ { ""Area"": ""a3"", ""Value"": ""p3"" } ], ""Description.vi"": ""d2"", ""Type"": ""t2"" }
          ] }";

        [TestCase("$.Contact[*]")]
        [TestCase("$.Contact")]
        public void Issue191_FlattenByJsonPathKeepsRootFields(string path)
        {
            using (var r = ChoJSONReader.LoadText(Json191)
                .Configure(c => c.FlattenNode = true)
                .Configure(c => c.NestedKeySeparator = '~')
                .Configure(c => c.FlattenByJsonPath = path)
                .Configure(c => c.FlattenByNodeName = "PostCode"))
            {
                var rows = r.Cast<IDictionary<string, object>>().ToArray();
                Assert.That(rows.Length, Is.EqualTo(3));
                Assert.That(rows.Select(x => x["BrandId"]), Is.EqualTo(new[] { "B1", "B1", "B1" }));
                Assert.That(rows.Select(x => x["Value"]), Is.EqualTo(new[] { "c1", "c1", "c2" }));
                Assert.That(rows.Select(x => x["Type"]), Is.EqualTo(new[] { "t1", "t1", "t2" }));
                Assert.That(rows.Select(x => x["PostCode~Area"]), Is.EqualTo(new[] { "a1", "a2", "a3" }));
                Assert.That(rows.Select(x => x["PostCode~Value"]), Is.EqualTo(new[] { "p1", "p2", "p3" }));
            }
        }

        // #191: a JSON key that itself contains '.' ("Description.vi") is treated as a nested path by dynamic
        // records (ChoETLSettings.KeySeparator, '.' by default). Documented workaround: use a key separator
        // that does not occur in the data.
        [Test]
        public void Issue191_LiteralDottedKeysWithCustomKeySeparator()
        {
            var previous = ChoETLSettings.KeySeparator;
            try
            {
                ChoETLSettings.KeySeparator = '\u001F';
                using (var r = ChoJSONReader.LoadText(Json191)
                    .Configure(c => c.FlattenNode = true)
                    .Configure(c => c.NestedKeySeparator = '~')
                    .Configure(c => c.FlattenByJsonPath = "$.Contact[*]")
                    .Configure(c => c.FlattenByNodeName = "PostCode"))
                {
                    var rows = r.Cast<IDictionary<string, object>>().ToArray();
                    Assert.That(rows.Select(x => x["Description.vi"]), Is.EqualTo(new[] { "d1", "d1", "d2" }));
                }
            }
            finally
            {
                ChoETLSettings.KeySeparator = previous;
            }
        }
    }
}
