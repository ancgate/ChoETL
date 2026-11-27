using System.Linq;
using System.Text;
using ChoETL;
using NUnit.Framework;

namespace ChoETL.Tests
{
    [TestFixture]
    public class SmokeTests
    {
        [Test]
        public void CsvRoundTrip()
        {
            string csv = "Id,Name\n1,Tom\n2,Carl";
            var recs = ChoCSVReader.LoadText(csv).WithFirstLineHeader().ToArray();
            Assert.That(recs.Length, Is.EqualTo(2));
            Assert.That((string)recs[1].Name, Is.EqualTo("Carl"));

            var sb = new StringBuilder();
            using (var w = new ChoCSVWriter(sb).WithFirstLineHeader())
                w.Write(recs);
            Assert.That(sb.ToString().Replace("\r\n", "\n").TrimEnd(), Is.EqualTo(csv));
        }
    }
}
