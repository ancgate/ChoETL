using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ChoETL;
using NUnit.Framework;

namespace ChoETL.Tests
{
    /// <summary>
    /// Regression tests for CSV / TSV issues reported on https://github.com/Cinchoo/ChoETL/issues.
    /// </summary>
    [TestFixture]
    public class CsvIssueTests
    {
        static string Normalize(string s) => s.Replace("\r\n", "\n");

        // ---------- #330: property-level [ChoTypeConverter] must win over built-in converters ----------

        public class ImpliedCentsConverter : IChoValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var text = value as string;
                if (string.IsNullOrWhiteSpace(text))
                    return 0m;
                return decimal.Parse(text.Trim(), CultureInfo.InvariantCulture) / 100m;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => ((long)((decimal)value * 100m)).ToString(CultureInfo.InvariantCulture);
        }

        public class Payment
        {
            [ChoCSVRecordField(1)]
            public string Name { get; set; }

            [ChoCSVRecordField(2)]
            [ChoTypeConverter(typeof(ImpliedCentsConverter))]
            public decimal Amount { get; set; }
        }

        [Test]
        public void Issue330_PropertyLevelTypeConverterIsUsedForDecimal_Csv()
        {
            var recs = ChoCSVReader<Payment>.LoadText("JANE DOE,0000291318").ToArray();
            Assert.That(recs[0].Amount, Is.EqualTo(2913.18m));
        }

        public class FixedPayment
        {
            [ChoFixedLengthRecordField(0, 8)]
            public string Name { get; set; }

            [ChoFixedLengthRecordField(8, 10)]
            [ChoTypeConverter(typeof(ImpliedCentsConverter))]
            public decimal Amount { get; set; }
        }

        [Test]
        public void Issue330_PropertyLevelTypeConverterIsUsedForDecimal_FixedLength()
        {
            var recs = ChoFixedLengthReader<FixedPayment>.LoadText("JANE DOE0000291318").ToArray();
            Assert.That(recs[0].Amount, Is.EqualTo(2913.18m));
        }

        public class NachaLikeEntry
        {
            [ChoFixedLengthRecordField(0, 8)]
            public string Name { get; set; }

            // Same declaration as ChoETL.NACHA's ChoNACHAEntryDetailRecord.Amount
            [ChoFixedLengthRecordField(8, 10)]
            [ChoTypeConverter(typeof(ChoCustomExprConverter), Parameters = @"'v => Decimal.Parse(v)/100.00', 'v => v.ToString(""#.00"").Replace(""."", """")'")]
            public decimal Amount { get; set; }
        }

        [Test]
        public void Issue330_CustomExprConverterWithParametersIsUsedForDecimal()
        {
            var recs = ChoFixedLengthReader<NachaLikeEntry>.LoadText("JANE DOE0000291318").ToArray();
            Assert.That(recs[0].Amount, Is.EqualTo(2913.18m));
        }

        [ChoFixedLengthRecordObject(18, ObjectValidationMode = ChoObjectValidationMode.ObjectLevel)]
        [ChoRecordTypeCode("6")]
        public class ManifoldEntry
        {
            [ChoFixedLengthRecordField(0, 1)]
            public string RecordType { get; set; }

            [ChoFixedLengthRecordField(1, 7)]
            public string Name { get; set; }

            [ChoFixedLengthRecordField(8, 10)]
            [ChoTypeConverter(typeof(ChoCustomExprConverter), Parameters = @"'v => Decimal.Parse(v)/100.00', 'v => v.ToString(""#.00"").Replace(""."", """")'")]
            public decimal Amount { get; set; }
        }

        [ChoFixedLengthRecordObject(18, ObjectValidationMode = ChoObjectValidationMode.ObjectLevel)]
        [ChoRecordTypeCode("9")]
        public class ManifoldControl
        {
            [ChoFixedLengthRecordField(0, 1)]
            public string RecordType { get; set; }

            [ChoFixedLengthRecordField(1, 17)]
            public decimal Total { get; set; }
        }

        [Test]
        public void Issue330_CustomExprConverterIsUsedByManifoldReader()
        {
            string data = "6JANE DO0000291318\n900000000000291318";
            using var reader = new ChoManifoldReader(new StringReader(data))
                .WithRecordSelector(0, 1, typeof(ManifoldEntry), typeof(ManifoldControl));
            reader.AfterRecordConfigurationConstruct += (o, e) =>
            {
                if (e.Configuration is ChoFileRecordConfiguration fc)
                    fc.FieldValueTrimOption = ChoFieldValueTrimOption.Trim;
            };
            reader.Configuration.ObjectValidationMode = ChoObjectValidationMode.ObjectLevel;
            var recs = reader.Cast<object>().ToArray();
            var entry = recs.OfType<ManifoldEntry>().Single();
            Assert.That(entry.Amount, Is.EqualTo(2913.18m));
            Assert.That(recs.OfType<ManifoldControl>().Single().Total, Is.EqualTo(291318m));
        }

        // ---------- #320: WriteHeader + WriteFields must emit the header ----------

        [Test]
        public void Issue320_WriteHeaderThenWriteFieldsEmitsHeader()
        {
            using var writer = new StringWriter();
            using (var w = new ChoCSVWriter(writer).WithFirstLineHeader())
            {
                w.WriteHeader("A", "B");
                w.WriteFields("A1", "B1");
                w.WriteFields("A2", "B2");
            }
            Assert.That(Normalize(writer.ToString()).TrimEnd(), Is.EqualTo("A,B\nA1,B1\nA2,B2"));
        }

        // ---------- #327: RFC 4180 doubled quotes are unescaped ----------

        [Test]
        public void Issue327_DoubledQuoteInsideQuotedFieldIsUnescaped()
        {
            string csv = "A,B,C\n,\"Some Value In My Column\"\"With Two Double Quotes\",";
            var rec = ChoCSVReader.LoadText(csv).WithFirstLineHeader().First();
            Assert.That((string)rec.B, Is.EqualTo("Some Value In My Column\"With Two Double Quotes"));
        }

        // ---------- #307 / #284: quoted header + POCO ----------

        public class Account
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        [Test]
        public void Issue307_QuotedHeaderMapsToPoco()
        {
            string csv = "\"Id\",\"Name\"\n\"1\",\"Tom\"\n\"2\",\"Carl\"\n\"3\",\"Mark\"";
            var recs = new ChoCSVReader<Account>(new StringReader(csv)).WithFirstLineHeader().ToArray();
            Assert.That(recs.Select(r => r.Id), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(recs.Select(r => r.Name), Is.EqualTo(new[] { "Tom", "Carl", "Mark" }));
        }

        [Test]
        public void Issue307_QuotedHeaderMapsToPoco_WithQuoteAllFields()
        {
            string csv = "\"Id\",\"Name\"\n\"1\",\"Tom\"";
            var recs = new ChoCSVReader<Account>(new StringReader(csv)).WithFirstLineHeader().QuoteAllFields().ToArray();
            Assert.That(recs[0].Id, Is.EqualTo("1"));
            Assert.That(recs[0].Name, Is.EqualTo("Tom"));
        }

        // ---------- #289: TSV with empty middle field ----------

        [Test]
        public void Issue289_TsvBlankFieldValue()
        {
            using var reader = ChoTSVReader.LoadText("A\t\tB").ErrorMode(ChoErrorMode.ThrowAndStop);
            var rec = reader.First();
            Assert.That((string)rec.Column1, Is.EqualTo("A"));
            Assert.That((object)rec.Column2, Is.Null.Or.EqualTo(string.Empty));
            Assert.That((string)rec.Column3, Is.EqualTo("B"));
        }

        // ---------- #135: quoted JSON with escaped quotes and commas ----------

        public class Settlement
        {
            [ChoCSVRecordField(FieldName = "SomeField1")]
            public string SomeField1 { get; set; }

            [ChoCSVRecordField(FieldName = "Split Payment Data", QuoteField = true)]
            public string SplitPaymentData { get; set; }
        }

        [Test]
        public void Issue135_QuotedFieldWithEscapedQuotesAndCommas()
        {
            string csv = "SomeField1,Split Payment Data\nvalue1,\"{\"\"split.amount\"\":\"\"1794\"\",\"\"split.currencyCode\"\":\"\"USD\"\"}\"";
            var rec = ChoCSVReader<Settlement>.LoadText(csv).WithFirstLineHeader().First();
            Assert.That(rec.SplitPaymentData, Is.EqualTo("{\"split.amount\":\"1794\",\"split.currencyCode\":\"USD\"}"));
        }

        // ---------- #85: Guid properties are written ----------

        public class WithGuid
        {
            public int Id { get; set; }
            public Guid Key { get; set; }
        }

        [Test]
        public void Issue85_GuidPropertyIsWritten()
        {
            var g = Guid.Parse("cc6f0116-589a-4cf1-8605-a4eb6ab3bd34");
            var sb = new StringBuilder();
            using (var w = new ChoCSVWriter<WithGuid>(sb).WithFirstLineHeader().QuoteAllFields())
                w.Write(new[] { new WithGuid { Id = 10, Key = g } });
            Assert.That(Normalize(sb.ToString()).TrimEnd(), Is.EqualTo("Id,Key\n\"10\",\"cc6f0116-589a-4cf1-8605-a4eb6ab3bd34\""));
        }

        // ---------- #245: IgnoreColumnsWithEmptyHeader must not shift values ----------

        [Test]
        public void Issue245_IgnoreColumnsWithEmptyHeaderKeepsValuesAligned()
        {
            string csv = "A,,B,,C\n1,x,2,y,3";
            var rec = ChoCSVReader.LoadText(csv).WithFirstLineHeader()
                .Configure(c => c.FileHeaderConfiguration.IgnoreColumnsWithEmptyHeader = true)
                .First();
            var dict = (IDictionary<string, object>)rec;
            Assert.That(dict.Keys, Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(dict["A"], Is.EqualTo("1"));
            Assert.That(dict["B"], Is.EqualTo("2"));
            Assert.That(dict["C"], Is.EqualTo("3"));
        }

        // ---------- #254: duplicate column names are auto-incremented ----------

        [Test]
        public void Issue254_AutoIncrementDuplicateColumnNames()
        {
            string csv = "Id,Name,Name\n1,a,b";
            var rec = ChoCSVReader.LoadText(csv).WithFirstLineHeader()
                .AutoIncrementDuplicateColumnNames(0, true)
                .First();
            var dict = (IDictionary<string, object>)rec;
            Assert.That(dict.Count, Is.EqualTo(3));
            Assert.That(dict.Values, Is.EqualTo(new object[] { "1", "a", "b" }));
        }

        // ---------- #87: CSV headers with spaces -> XML ----------

        [Test]
        public void Issue87_CsvHeaderWithSpacesToXml()
        {
            string csv = "Company Name,City\nAcme,Paris";
            var sb = new StringBuilder();
            using (var r = ChoCSVReader.LoadText(csv).WithFirstLineHeader())
            using (var w = new ChoXmlWriter(sb).WithRootName("Records").WithNodeName("Record"))
                w.Write(r);
            var xml = sb.ToString();
            Assert.That(xml, Does.Contain("Acme"));
            Assert.That(xml, Does.Contain("Paris"));
        }

        // ---------- #266: quote only non-null values ----------

        public class Row266
        {
            public string A { get; set; }
            public string B { get; set; }
            public string C { get; set; }
            public string D { get; set; }
        }

        [Test]
        public void Issue266_QuoteNonNullValuesOnly()
        {
            var sb = new StringBuilder();
            using (var w = new ChoCSVWriter<Row266>(sb)
                .WithDelimiter("|")
                .QuoteAllFields()
                .QuoteNullValues(false))
            {
                w.Write(new Row266 { A = "a", B = null, C = "", D = "b" });
            }
            Assert.That(Normalize(sb.ToString()).TrimEnd(), Is.EqualTo("\"a\"||\"\"|\"b\""));
        }

        [Test]
        public void Issue266_DefaultStillQuotesNulls()
        {
            var sb = new StringBuilder();
            using (var w = new ChoCSVWriter<Row266>(sb).WithDelimiter("|").QuoteAllFields())
                w.Write(new Row266 { A = "a", B = null, C = "", D = "b" });
            Assert.That(Normalize(sb.ToString()).TrimEnd(), Is.EqualTo("\"a\"|\"\"|\"\"|\"b\""));
        }
    }

    [TestFixture]
    public class FixedLengthIssueTests
    {
        public class BoolRecord
        {
            [ChoFixedLengthRecordField(0, 1)] public bool TestBool1 { get; set; }
            [ChoFixedLengthRecordField(1, 1)] public bool TestBool2 { get; set; }
            [ChoFixedLengthRecordField(2, 1)] public bool TestBool3 { get; set; }
            [ChoFixedLengthRecordField(3, 1)] public bool TestBool4 { get; set; }
            [ChoFixedLengthRecordField(4, 1)] public string TestString { get; set; }
        }

        // #219: 0/1 booleans in fixed-length files
        [Test]
        public void Issue219_ZeroOneBooleansAreRead()
        {
            string data = "0101a\n1111b\n1011c\n0000d\n1000e";
            var recs = ChoFixedLengthReader<BoolRecord>.LoadText(data).ToArray();
            Assert.That(recs.Length, Is.EqualTo(5));
            Assert.That(recs.Select(r => (r.TestBool1, r.TestBool2, r.TestBool3, r.TestBool4)), Is.EqualTo(new[]
            {
                (false, true, false, true),
                (true, true, true, true),
                (true, false, true, true),
                (false, false, false, false),
                (true, false, false, false),
            }));
            Assert.That(recs.Select(r => r.TestString), Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
        }

        public class Line
        {
            [ChoFixedLengthRecordField(0, 3)] public string Code { get; set; }
        }

        // #183: optional newline at end of file
        [Test]
        public void Issue183_AddEOLDelimiterAtEOF()
        {
            var sb = new StringBuilder();
            using (var w = new ChoFixedLengthWriter<Line>(sb).Configure(c => c.AddEOLDelimiterAtEOF = true))
                w.Write(new[] { new Line { Code = "abc" }, new Line { Code = "def" } });
            Assert.That(sb.ToString(), Is.EqualTo("abc" + Environment.NewLine + "def" + Environment.NewLine));
        }
    }
}
