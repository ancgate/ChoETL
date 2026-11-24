# Upstream issue triage (Cinchoo/ChoETL)

All **76 open issues** on https://github.com/Cinchoo/ChoETL/issues (as of 2026-11-24) were read (body and comments) and triaged against this branch.

| Status | Meaning | Count |
|---|---|---|
| **FIXED** | Reproduced and fixed in this branch, with a regression test in `src/Test/ChoETL.Tests` (or a build/packaging change) | 27 |
| **VERIFIED** | Already fixed upstream; a regression test confirms it on current source | 17 |
| **ANSWERED** | Usage question or by-design behaviour; the answer is below, no code change | 15 |
| **PARTIAL** | Part fixed or verified, part needs the reporter's data or more work | 5 |
| **DEFERRED** | Feature work too large for this pass (see "Deferred work") | 11 |
| **N/A** | Not a code issue | 1 |

Regression safety: the upstream test suites (CSV reader/writer, JSON reader/writer, XML reader, Parquet reader/writer, about 900 tests) were compiled for net8.0 and run before and after every change. None of the fixes caused a new failure. Two upstream Parquet expectations were updated because they encoded the old buggy output (see "Behaviour changes").

## Packaging, build and dependencies

| # | Title | Status | Notes |
|---|---|---|---|
| 332 | Upgrade Parquet.NET to v6+ | DEFERRED | Parquet.Net 5/6 only ship net8.0/net10.0 builds and removed `DataColumn`, which ChoETL exposes publicly (`ChoRowGroupEventArgs`). See "Deferred work". Moved to Parquet.Net 4.25.0 (latest 4.x). |
| 331 | Snappier 1.1.1 vulnerable | FIXED | Direct `Snappier 1.3.3` reference overrides the transitive 1.1.1 (pulled in via IronCompress). NuGet audit (transitive) is enabled and reports no vulnerable packages. |
| 328 | Migrate System.Data.SqlClient to Microsoft.Data.SqlClient | FIXED | Every target uses Microsoft.Data.SqlClient 6.1.8. Previously only net8+ did, so the `Bcp(SqlConnection, ...)` overloads took *different* types per framework. Only the EF6-based SqlServer plugin still pulls System.Data.SqlClient, because EntityFramework 6 itself depends on it. |
| 267 | Vulnerability in System.Data.SqlClient | FIXED | Same as #328: no longer a dependency of the core, JSON, Parquet or YAML packages. |
| 323 | ChoETL.Parquet under .NET Standard | FIXED | ChoETL.Parquet ships `netstandard2.0`, `net6.0`, `net8.0` and `net10.0` (checked in the .nupkg). |
| 65 | Combine / multi-target frameworks | FIXED | One package per library now covers netstandard2.0, net6.0, net8.0 and net10.0. The separate .NET Framework packages (ChoETL, ChoETL.JSON) still exist; netstandard2.0 also serves .NET Framework 4.6.1+. |
| 238 | SourceLink | FIXED | `PublishRepositoryUrl`, `EmbedUntrackedSources`, `.snupkg` symbol packages, `ContinuousIntegrationBuild` on CI. |
| 231 | NU1603 about Newtonsoft.Json | FIXED | Dependency is now Newtonsoft.Json 13.0.4 (no 12.0.0 floor). |
| 126 | XML comments missing | PARTIAL | XML documentation files are now generated and packed, so existing comments reach IntelliSense. Most members still have no comments. |
| 60 | Unit test framework | FIXED | New cross-platform NUnit project `src/Test/ChoETL.Tests` (net8.0, net10.0) plus a GitHub Actions workflow. |
| 130 | Test cases removed / commented out | FIXED | Same as #60. The legacy .NET Framework test programs are unchanged but were verified on net8 (see above). |
| 297 | BadImageFormatException System.ComponentModel.DataAnnotations | ANSWERED | .NET Framework deployment/binding problem: deploy the referenced System.ComponentModel.Annotations assembly or use the netstandard/net8 packages. |
| 208 | MissingMethodException get_RecordSelector | ANSWERED | ChoETL and ChoETL.JSON packages of different versions were mixed. Keep all ChoETL.* packages on the same version. |
| 302 | JSON, XML and YAML ambiguity | ANSWERED | .NET Framework and .NETStandard packages were mixed. Reference only the `*.NETStandard` packages (plus ChoETL.Yaml / ChoETL.Parquet). |
| 326 | Migration to organization | N/A | Repository ownership question. |

## Core / CSV / TSV / fixed length

| # | Title | Status | Notes |
|---|---|---|---|
| 330 | Property-level `[ChoTypeConverter]` bypassed (NACHA Amount) | VERIFIED | Not reproducible on current source. Tests cover a custom `IChoValueConverter` and the exact NACHA `ChoCustomExprConverter` declaration via CSV, fixed-length and `ChoManifoldReader` with a record selector. Likely fixed in 1.2.1.71/72. |
| 327 | Doubled quotes in quoted fields | FIXED | Auto-quote mode (the default) now follows RFC 4180: enclosing quotes are removed and `""` becomes `"`. Previously the outer quotes stayed unless `MayHaveQuotedFields()` was set. |
| 307 | Missing 'Id' CSV column (quoted header) | FIXED | Quoted header names (`"Id","Name"`) are unquoted, so they bind to POCO members. |
| 284 | Missing field value with POCO (quoted CSV) | FIXED | Same root cause as #307. |
| 266 | CSV write: quote non-null only | FIXED | New `QuoteNullValues(false)` / `ChoCSVRecordConfiguration.QuoteNullValues`: with quoting on, null is written as an empty unquoted field and `""` stays quoted. Default `true` keeps existing output. |
| 219 | Fixed-length bool `0`/`1` not read | FIXED | Since 1.2.1.67 the default `TrueOrFalse` format threw on `0`/`1` (before that it returned `false`). Reading now tries the configured format first, then 1/0, Y/N, T/F, true/false, yes/no. Invalid text still throws. |
| 320 | No header when using WriteFields | VERIFIED | Test passes. |
| 289 | TSV blank field | VERIFIED | Test passes. |
| 135 | Escaped quote before comma (QuoteField) | VERIFIED | Test passes. |
| 245 | IgnoreColumnsWithEmptyHeader shifts values | VERIFIED | Test passes. |
| 254 | AutoIncrementDuplicateColumnNames | VERIFIED | Tested for CSV and CSV to Parquet with the reporter's `$$` / `$EOL$` configuration. |
| 87 | CSV to XML: spaces in headers | VERIFIED | Test passes. |
| 85 | Guid not converted | VERIFIED | Guid columns are written. Note: `QuoteAllFields()` does not quote the header; use `QuoteAllHeaders()` for that. |
| 183 | No CRLF at end of fixed-length file | VERIFIED | `AddEOLDelimiterAtEOF = true`. |
| 149 | IgnoredFields not settable | VERIFIED | Property has a setter. |
| 81 | Generic `ChoCSVRecordFieldConfiguration<T>` | VERIFIED | Available (`WithField(x => x.Prop, ...)` and `ChoCSVRecordConfiguration<T>`). |
| 321 | NestedColumnSeparator removed | FIXED | Restored as an `[Obsolete]` alias of `NestedKeySeparator`. The maintainer's own answers to #304 and #206 used the old name. |
| 255 | Compare reports rows as deleted and added | ANSWERED | `Compare` requires both inputs sorted by the key. |
| 128 | Text column with line feeds | ANSWERED | Set `MayContainEOLInData(true)`; such values must be quoted. |
| 106 | Headers from later JSON records | VERIFIED | Use `WithMaxScanRows(n)` on the CSV writer and either `UseJsonSerialization()` or `Configure(c => c.MaxScanRows = n)` on the JSON reader. Test added. |
| 29 | Header spanning two rows | ANSWERED | Not supported. Use `HeaderLineAt(n)` plus explicit `WithField(...)` names. |
| 30 | Logging override | ANSWERED | `ChoETLFrxBootstrap.Log = msg => logger.LogInformation(msg);`. The Release packages previously compiled with `TRACETRACE` instead of `TRACE`, which dropped all `Trace.*` output; that is fixed. |
| 31 | Summary method | DEFERRED | Feature request. |
| 119 | How to handle large files | ANSWERED | Readers and writers stream: don't call `ToArray()`/`ToList()`, pipe reader into writer (`w.Write(r)`), and for SQL Server use the `Bcp(...)` helpers (SqlBulkCopy). For Parquet, tune `RowGroupSize`. |
| 94 | Extract data from SqlServer | ANSWERED | `new ChoParquetWriter(path).Write(sqlCommand.ExecuteReader())` (or CSV/JSON writers) streams the `IDataReader`. NULL handling for this path was fixed (#244). |

## JSON

| # | Title | Status | Notes |
|---|---|---|---|
| 325 | Convert JSON to CSV issue 2 | FIXED | When a member is an array in one record and a scalar in another, the CSV header kept only one shape and the other record's values were silently dropped. Missing flattened keys are now appended to the header. Existing column order is unchanged. |
| 206 | Flatten JSON array in array | FIXED | `FlattenByNodeName` over `[[0,24],[1,2]]` gives 2 records (`TimeRange` = inner array) instead of 4 single-value records. |
| 304 | Merging nested arrays when arrays are empty | VERIFIED | Reporter's configuration with an empty array works. Test added. |
| 303 | ChoReaderException 'Failed to parse '"'' | PARTIAL | Best-guess repro (value consisting of a single `"`) passes. The reporter's `result.json` is needed to go further. |
| 263 | JSON to CSV to JSON loses arrays | DEFERRED | CSV cannot represent empty arrays. Round-trip needs explicit field definitions (maintainer's fiddle). |
| 194 | Generate JSON path | DEFERRED | Feature request. |
| 191 | Flatten complex JSON structure | PARTIAL | Parts (a) and (b) were fixed upstream (1.2.1.45 / 1.2.1.48). Part (c) (root fields lost when the flatten node's items have their own arrays) is not addressed. |
| 165 | CSV to JSON including null | VERIFIED | `IgnoreFieldValueMode(ChoIgnoreFieldValueMode.Null)` on the JSON writer. |
| 147 | Merging same JSON child keys | ANSWERED | Use `WithField("Players", jsonPath: "$..Players[*].player", valueConverter: o => string.Join(",", (IEnumerable<object>)o))`. |
| 141 | Nested JSON to CSV rows | DEFERRED | Generic "every nested array becomes rows" for unknown schemas. The workaround is `FlattenBy` per branch. |
| 322 | Recursive call in YamlWriter | FIXED | The YAML fix was already upstream (verified). The same self-recursion existed in `ChoJSONWriter.Write(DataTable)` with `UseJSONSerialization` and `ChoXmlWriter.Write(DataTable)` with `UseXmlSerialization`, causing StackOverflowException. Both fixed, with tests. |

## XML

| # | Title | Status | Notes |
|---|---|---|---|
| 316 | ChoXmlWriter: array item names | FIXED | New `ChoXmlRecordConfiguration.ArrayItemNodeName` (for example `"item"`) names object items of every array when writing dynamic records. |
| 131 | Memory leak in XML reader | FIXED | `ToObject(XmlNode, ..., unknownNode)` added the handler to a cached, shared XmlSerializer on every call. A test checks that 2,000 `LoadXElement` calls generate no new assemblies. |
| 173 | Unknown default xmlns | VERIFIED | MSBuild-style project file with a default namespace binds. Test added. |
| 203 | PackageReference Include not loaded | ANSWERED | POCO lists use XmlSerializer semantics: a list of repeated, unwrapped elements needs `[XmlElement]`. Test documents the pattern. |
| 177 | Make `[XmlElement]` the default | DEFERRED | Auto-detecting wrapped vs unwrapped lists conflicts with serializer caching per type. |

## Parquet

| # | Title | Status | Notes |
|---|---|---|---|
| 314 | CSV to Parquet nullable types | FIXED | Nullable primitives (`long?`, `double?`, ...) were forced to string columns (a Parquet.Net 3 workaround). |
| 300 | Field configuration not applied on write | FIXED | Same root cause: `WithField(fieldType: typeof(double?))` now produces a `double` (nullable) column. |
| 257 | InvalidCastException for value 0 | FIXED | Two bugs: (1) **core**: `ChoDynamicObject` treated any member named `Contains...` (for example `ContainsComments`) as a "does key exist" helper, so the value read back as `false` for every record, in every format; (2) Parquet inferred column types from the first record only, as non-nullable. Inferred columns are now nullable, and `WithMaxScanRows(n)` widens numeric types (1 then 2.5 gives double). |
| 244 | IDataReader and DBNull | FIXED | `AllowDBNull` columns become nullable Parquet columns and DBNull is written as null. Previously NULL DateTime became `0001-01-01` and NULL strings became `""`. The same applies to `Write(DataTable)`. (`LiteParsing` is a no-op for Parquet.) |
| 292 | DB Null exception | FIXED | Same as #244. |
| 232 | Nullable fields NullReferenceException | FIXED | Nullable POCO fields round-trip. Null `TimeSpan?` was written as `"00:00:00"` and null `ChoCurrency` as 0. |
| 288 | Writing dates as Parquet datetime | FIXED | DateTime is written as a timestamp. The documented `TreatDateTimeAsDateTimeOffset = true` threw `NotSupportedException` with Parquet.Net 4 ("DateTimeOffset support was dropped"); it now writes a UTC timestamp. |
| 293 | MissingMethodException with IgnoreDictionaryFieldPrefix | PARTIAL | `IgnoreDictionaryFieldPrefix` works with `UseNestedKeyFormat(true)` (test). Without nested key format, nested objects crashed with `NotImplementedException`; they are now stored as JSON text. The exact MissingMethodException needs the reporter's payload. |
| 152 | DateTimeOffset from CSV | VERIFIED | Test passes (written as text by default; `TreatDateTimeOffsetAsString = false` writes UTC timestamps). |
| 202 | Different array lengths | VERIFIED | Missing elements are now written as null rather than 0 (upstream test expectation updated). |
| 256 | Datetime handling (Python files) | PARTIAL | The DateTime/DateTimeOffset write part is fixed (#288). `TreatBigIntegersAsDates` on Python-written files needs a sample file. |
| 298 | Gzip makes file larger | ANSWERED | Small files and already-dictionary-encoded columns don't compress well. Try `CompressionMethod.Snappy`/`Zstd` and a larger `RowGroupSize`. |
| 250 | Stream compatibility / size | ANSWERED | Writers stream row groups (`RowGroupSize`). Use compression for size. |
| 246 | Flattened Parquet back to POCO | ANSWERED | Read with `UseNestedKeyFormat()` (same separator as when writing) so keys are un-flattened before `ConvertToObject<T>()`. |
| 315 | Header with field names and types | ANSWERED | Use `reader.Configuration.<Format>RecordFieldConfigurations` (`Name`, `FieldType`) after the first record, or the `MembersDiscovered` event. |
| 139 | Nested POCOs as Parquet | DEFERRED | Needs Parquet LIST/STRUCT columns (see Deferred work). |
| 159 | Nested array (not flat) | DEFERRED | Same as #139. |
| 285 | Nested array (array<struct>) | DEFERRED | Same as #139. |

## Other formats

| # | Title | Status | Notes |
|---|---|---|---|
| 207 | HL7 support | DEFERRED | See the format gap list in the main summary. |
| 32 | Excel support | DEFERRED | Upstream marked it `wontfix`; see the format gap list. |

## Behaviour changes to be aware of

1. **CSV auto-quote mode** strips enclosing quotes from header names and values (RFC 4180). Use `QuoteAllFields(false)` to keep the raw text.
2. **Booleans**: `0`/`1`, `Y`/`N` and similar are accepted when reading, whatever the configured `BooleanFormat` (which still controls writing).
3. **Microsoft.Data.SqlClient**: `Bcp(...)` / `SqlBulkCopy` APIs on netstandard2.0 and net6.0 now take `Microsoft.Data.SqlClient` types (previously `System.Data.SqlClient`).
4. **Parquet**:
   - nullable numerics are real numeric columns, not strings;
   - columns inferred from dynamic data are nullable;
   - `IDataReader`/`DataTable` NULLs stay null;
   - `TreatDateTimeAsDateTimeOffset` writes UTC timestamps;
   - nested objects without `UseNestedKeyFormat` are written as JSON text.
5. **CSV header scan** may add columns that previously had their values dropped (#325).
6. **Release builds define `TRACE` again**, so ChoETL's `Trace` logging is active.

## Deferred work

* **Parquet.Net 6 (#332)**: only for net8.0/net10.0 (netstandard2.0/net6.0 must stay on 4.x). Steps:
  - replace `DataColumn` reads/writes with `ReadAsync<T>`/`WriteAsync<T>`, dispatched by field CLR type;
  - move `CompressionMethod` into `ParquetOptions`;
  - add a ChoETL-owned column type for `ChoRowGroupEventArgs.Records`, which is a public API break;
  - re-baseline the Parquet tests for v6's DateTime/decimal semantics.
* **Nested Parquet (#139, #159, #285)**: map lists and objects to `ListField`/`StructField` instead of flattening.
* **BSON plugin**: `src/ChoETL.BSON` did not compile before these changes either (it references Avro types and has access-modifier errors). It is retargeted but still excluded from the solution.
