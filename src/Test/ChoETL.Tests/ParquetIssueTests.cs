using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ChoETL;
using NUnit.Framework;
using Parquet;
using Parquet.Schema;

namespace ChoETL.Tests
{
    [TestFixture]
    public class ParquetIssueTests
    {
        static Dictionary<string, DataField> ReadSchema(byte[] parquet)
        {
            using var ms = new MemoryStream(parquet);
            using var reader = ParquetReader.CreateAsync(ms).GetAwaiter().GetResult();
            return reader.Schema.GetDataFields().ToDictionary(f => f.Name);
        }

        static object[] ReadAll(byte[] parquet)
        {
            using var r = new ChoParquetReader(new MemoryStream(parquet));
            return r.ToArray();
        }

        // ---------- #314: MapParquetType to nullable primitives ----------

        [Test]
        public void Issue314_MapParquetTypeToNullablePrimitives()
        {
            string csv = "Id,Amount,Code\n1,2.5,A\n2,,B";
            var ms = new MemoryStream();
            using (var r = ChoCSVReader.LoadText(csv).WithFirstLineHeader()
                .WithField("Id", fieldType: typeof(long))
                .WithField("Amount", fieldType: typeof(double))
                .WithField("Code", fieldType: typeof(char)))
            using (var w = new ChoParquetWriter(ms).Configure(c => c.LeaveStreamOpen = true).Configure(c => c.MapParquetType = type =>
            {
                if (type == typeof(char)) return typeof(string);
                if (type == typeof(long)) return typeof(long?);
                if (type == typeof(double)) return typeof(double?);
                return type;
            }))
            {
                w.Write(r);
            }
            var schema = ReadSchema(ms.ToArray());
            Assert.That(schema["Id"].ClrType, Is.EqualTo(typeof(long)));
            Assert.That(schema["Id"].IsNullable, Is.True);
            Assert.That(schema["Amount"].ClrType, Is.EqualTo(typeof(double)));
            Assert.That(schema["Amount"].IsNullable, Is.True);
            Assert.That(schema["Code"].ClrType, Is.EqualTo(typeof(string)));

            var recs = ReadAll(ms.ToArray());
            Assert.That(((IDictionary<string, object>)recs[1])["Amount"], Is.Null);
            Assert.That(Convert.ToDouble(((IDictionary<string, object>)recs[0])["Amount"]), Is.EqualTo(2.5));
        }

        // ---------- #300: WithField(fieldType: double?) on ChoParquetWriter<T> ----------

        public class Detail
        {
            public string Name { get; set; }
            public double? DetailAmount { get; set; }
            public double LinePrice { get; set; }
        }

        [Test]
        public void Issue300_WithFieldNullableDoubleIsWrittenAsDouble()
        {
            var ms = new MemoryStream();
            using (var w = new ChoParquetWriter<Detail>(ms, new ChoParquetRecordConfiguration { AutoDiscoverFieldTypes = false, LeaveStreamOpen = true }))
            {
                w.WithField(nameof(Detail.Name), fieldType: typeof(string));
                w.WithField(nameof(Detail.DetailAmount), fieldType: typeof(double?));
                w.WithField(nameof(Detail.LinePrice), fieldType: typeof(double));
                w.Write(new[] { new Detail { Name = "a", DetailAmount = null, LinePrice = 110.0 }, new Detail { Name = "b", DetailAmount = 1.5, LinePrice = 2 } });
            }
            var schema = ReadSchema(ms.ToArray());
            Assert.That(schema["DetailAmount"].ClrType, Is.EqualTo(typeof(double)));
            Assert.That(schema["DetailAmount"].IsNullable, Is.True);
            Assert.That(schema["LinePrice"].ClrType, Is.EqualTo(typeof(double)));
        }

        // ---------- #232: POCO with nullable fields ----------

        public class Nullables
        {
            public bool? B { get; set; }
            public decimal? D { get; set; }
            public int? I { get; set; }
            public DateTime? Dt { get; set; }
            public TimeSpan? Ts { get; set; }
            public long? L { get; set; }
        }

        [Test]
        public void Issue232_PocoWithNullableFields()
        {
            var ms = new MemoryStream();
            using (var w = new ChoParquetWriter<Nullables>(ms, new ChoParquetRecordConfiguration { NullValueHandling = ChoNullValueHandling.Null, LeaveStreamOpen = true }))
            {
                w.Write(new[]
                {
                    new Nullables { B = true, D = 1.5m, I = 3, Dt = new DateTime(2024, 1, 2), Ts = TimeSpan.FromMinutes(5), L = 9 },
                    new Nullables(),
                });
            }
            var recs = ReadAll(ms.ToArray());
            Assert.That(recs.Length, Is.EqualTo(2));
            var second = (IDictionary<string, object>)recs[1];
            Assert.That(second.Values.All(v => v == null), Is.True, string.Join(",", second.Select(kv => kv.Key + "=" + kv.Value)));
        }

        // ---------- #244 / #292: IDataReader with DBNull values ----------

        static DataTable NullableTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("DateTimeTs", typeof(DateTime));
            dt.Columns.Add("Notes", typeof(string));
            dt.Rows.Add(1, new DateTime(2022, 9, 23), "a");
            dt.Rows.Add(2, DBNull.Value, DBNull.Value);
            dt.Rows.Add(3, new DateTime(2022, 9, 24), DBNull.Value);
            return dt;
        }

        // (ChoParquetRecordConfiguration.LiteParsing is a static no-op; the LiteParsing code path in #244 no longer exists.)
        [Test]
        public void Issue244_DataReaderWithDBNull()
        {
            var ms = new MemoryStream();
            using (var w = new ChoParquetWriter(ms).Configure(c => c.LeaveStreamOpen = true))
                w.Write(NullableTable().CreateDataReader());
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Length, Is.EqualTo(3));
            Assert.That(recs[1]["DateTimeTs"], Is.Null);
            Assert.That(recs[1]["Notes"], Is.Null);
            Assert.That(recs[2]["Notes"], Is.Null);
            Assert.That(recs[0]["Notes"], Is.EqualTo("a"));
        }

        // ---------- #293: IgnoreDictionaryFieldPrefix with flattened JSON ----------

        const string Json293 = @"[{ ""id"": 1, ""user"": { ""name"": ""tom"", ""tags"": { ""a"": ""x"" } }, ""created"": ""2023-01-09T00:00:00Z"" },
                                  { ""id"": 2, ""user"": { ""name"": ""ann"", ""tags"": { ""a"": ""y"" } }, ""created"": ""2023-01-10T00:00:00Z"" }]";

        [Test]
        public void Issue293_IgnoreDictionaryFieldPrefix()
        {
            var ms = new MemoryStream();
            using (var r = new ChoJSONReader(new StringReader(Json293), new ChoJSONRecordConfiguration { FlattenNode = true }))
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
            Assert.That(ReadSchema(ms.ToArray()).Keys, Is.EquivalentTo(new[] { "id", "name", "a", "created" }));
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Select(r => r["name"]), Is.EqualTo(new[] { "tom", "ann" }));
        }

        [Test]
        public void Issue293_NestedObjectsWithoutNestedKeyFormatAreWrittenAsJson()
        {
            var ms = new MemoryStream();
            using (var r = new ChoJSONReader(new StringReader(Json293)))
            using (var w = new ChoParquetWriter(ms, new ChoParquetRecordConfiguration { LeaveStreamOpen = true }))
                w.Write(r);
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            var user = Newtonsoft.Json.Linq.JObject.Parse((string)recs[0]["user"]);
            Assert.That((string)user["name"], Is.EqualTo("tom"));
            Assert.That((string)user["tags"]["a"], Is.EqualTo("x"));
        }

        // ---------- #257: integer JSON values (0) mixed with other numbers ----------

        [Test]
        public void Issue257_ZeroValuedJsonField()
        {
            string json = @"[{ ""Id"": 1, ""ContainsComments"": 0, ""Score"": 1 }, { ""Id"": 2, ""ContainsComments"": 1, ""Score"": 2.5 }, { ""Id"": 3, ""ContainsComments"": 0, ""Score"": null }]";
            var ms = new MemoryStream();
            using (var r = ChoJSONReader.LoadText(json))
            using (var w = new ChoParquetWriter(ms).ErrorMode(ChoErrorMode.ThrowAndStop).Configure(c => c.LeaveStreamOpen = true))
                w.Write(r);
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Length, Is.EqualTo(3));
            Assert.That(Convert.ToInt64(recs[0]["ContainsComments"]), Is.EqualTo(0));
            Assert.That(Convert.ToInt64(recs[1]["ContainsComments"]), Is.EqualTo(1));
        }

        [Test]
        public void Issue257_ScannedRowsWidenNumericTypesAndKeepNulls()
        {
            string json = @"[{ ""Id"": 1, ""Score"": 1 }, { ""Id"": 2, ""Score"": 2.5 }, { ""Id"": 3, ""Score"": null }]";
            var ms = new MemoryStream();
            using (var r = ChoJSONReader.LoadText(json))
            using (var w = new ChoParquetWriter(ms).WithMaxScanRows(3).Configure(c => c.LeaveStreamOpen = true))
                w.Write(r);
            var schema = ReadSchema(ms.ToArray());
            Assert.That(schema["Score"].ClrType, Is.EqualTo(typeof(double)));
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Select(x => x["Score"]), Is.EqualTo(new object[] { 1d, 2.5d, null }));
        }

        [Test]
        public void Issue257_LaterNullsStayNullWithoutScanning()
        {
            string json = @"[{ ""Id"": 1, ""Count"": 5 }, { ""Id"": 2, ""Count"": null }]";
            var ms = new MemoryStream();
            using (var r = ChoJSONReader.LoadText(json))
            using (var w = new ChoParquetWriter(ms).Configure(c => c.LeaveStreamOpen = true))
                w.Write(r);
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs[1]["Count"], Is.Null);
        }

        // #288: the documented TreatDateTimeAsDateTimeOffset option used to throw with Parquet.Net 4
        [Test]
        public void Issue288_TreatDateTimeAsDateTimeOffsetWritesUtcTimestamp()
        {
            var ms = new MemoryStream();
            var local = new DateTime(2023, 1, 9, 10, 30, 0, DateTimeKind.Utc);
            using (var w = new ChoParquetWriter<Reported>(ms).Configure(c => c.TreatDateTimeAsDateTimeOffset = true).Configure(c => c.LeaveStreamOpen = true))
                w.Write(new[] { new Reported { Id = 1, date_reported = local }, new Reported { Id = 2 } });
            var schema = ReadSchema(ms.ToArray());
            Assert.That(schema["date_reported"].ClrType, Is.EqualTo(typeof(DateTime)));
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(((DateTime)recs[0]["date_reported"]).ToUniversalTime(), Is.EqualTo(local));
            Assert.That(recs[1]["date_reported"], Is.Null);
        }

        // ---------- #152: DateTimeOffset values read from CSV ----------

        [Test]
        public void Issue152_DateTimeOffsetFromCsv()
        {
            string csv = "Id,Created\n1,1900-01-01T00:00:00.0000000\n2,2021-08-31T10:00:00.0000000+02:00";
            var ms = new MemoryStream();
            using (var r = ChoCSVReader.LoadText(csv).WithFirstLineHeader()
                .WithField("Id", fieldType: typeof(int))
                .WithField("Created", fieldType: typeof(DateTimeOffset)))
            using (var w = new ChoParquetWriter(ms, new ChoParquetRecordConfiguration { CompressionMethod = CompressionMethod.Snappy, LeaveStreamOpen = true }))
                w.Write(r);
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Length, Is.EqualTo(2));
            Assert.That(recs[1]["Created"], Is.Not.Null);
        }

        // ---------- #288: DateTime written as a parquet timestamp, not text ----------

        public class Reported
        {
            public int Id { get; set; }
            public DateTime? date_reported { get; set; }
        }

        [Test]
        public void Issue288_DateTimeIsWrittenAsTimestamp()
        {
            var ms = new MemoryStream();
            using (var w = new ChoParquetWriter<Reported>(ms).Configure(c => c.TreatDateTimeAsString = false).Configure(c => c.LeaveStreamOpen = true))
                w.Write(new[] { new Reported { Id = 1, date_reported = new DateTime(2023, 1, 9) }, new Reported { Id = 2 } });
            var schema = ReadSchema(ms.ToArray());
            Assert.That(schema["date_reported"].ClrType, Is.EqualTo(typeof(DateTime)).Or.EqualTo(typeof(DateTimeOffset)));
        }

        // ---------- #254: duplicate CSV column names into parquet ----------

        [Test]
        public void Issue254_DuplicateCsvColumnsToParquet()
        {
            string csv = "Id$$Name$$Name$EOL$1$$a$$b$EOL$";
            var ms = new MemoryStream();
            using (var r = ChoCSVReader.LoadText(csv)
                .WithDelimiter("$$")
                .WithFirstLineHeader()
                .MayHaveQuotedFields()
                .AutoIncrementDuplicateColumnNames(0, true)
                .IgnoreCase(true)
                .WithEOLDelimiter("$EOL$"))
            using (var w = new ChoParquetWriter(ms).Configure(c => c.LeaveStreamOpen = true))
                w.Write(r);
            var recs = ReadAll(ms.ToArray()).Cast<IDictionary<string, object>>().ToArray();
            Assert.That(recs.Length, Is.EqualTo(1));
            Assert.That(recs[0].Count, Is.EqualTo(3));
            Assert.That(recs[0].Values.Select(v => v?.ToString()), Is.EqualTo(new[] { "1", "a", "b" }));
        }
    }
}
