﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace ChoETL
{
    public class ChoXmlReader<T> : ChoReader, IDisposable, IEnumerable<T>
        where T : class
    {
        //private TextReader _textReader;
        private StreamReader _sr;
        private XmlReader _xmlReader;
        private IEnumerable<XElement> _xElements;
        private bool _closeStreamOnDispose = false;
        private Lazy<IEnumerator<T>> _enumerator = null;
        private CultureInfo _prevCultureInfo = null;
        private bool _clearFields = false;
        public TraceSwitch TraceSwitch = ChoETLFramework.TraceSwitch;
        public event EventHandler<ChoRowsLoadedEventArgs> RowsLoaded;
        public event EventHandler<ChoEventArgs<IDictionary<string, Type>>> MembersDiscovered;

        public ChoXmlRecordConfiguration Configuration
        {
            get;
            private set;
        }

        public ChoXmlReader(ChoXmlRecordConfiguration configuration = null)
        {
            Configuration = configuration;
            Init();
        }

        public ChoXmlReader(string filePath, string defaultNamespace)
        {
            ChoGuard.ArgumentNotNullOrEmpty(filePath, "FilePath");

            Configuration = new ChoXmlRecordConfiguration();
            if (!defaultNamespace.IsNullOrWhiteSpace())
                Configuration.NamespaceManager.AddNamespace("", defaultNamespace);

            Init();

            _sr = new StreamReader(ChoPath.GetFullPath(filePath), Configuration.GetEncoding(filePath), false, Configuration.BufferSize);
            _xmlReader = XmlReader.Create(_sr,
                new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }, new XmlParserContext(null, Configuration.NamespaceManager, null, XmlSpace.None));
            _closeStreamOnDispose = true;
        }

        public ChoXmlReader(string filePath, ChoXmlRecordConfiguration configuration = null)
        {
            ChoGuard.ArgumentNotNullOrEmpty(filePath, "FilePath");

            Configuration = configuration;

            Init();

            _sr = new StreamReader(ChoPath.GetFullPath(filePath), Configuration.GetEncoding(filePath), false, Configuration.BufferSize);
            _xmlReader = XmlReader.Create(_sr,
                new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }, new XmlParserContext(null, Configuration.NamespaceManager, null, XmlSpace.None));
            _closeStreamOnDispose = true;
        }

        public ChoXmlReader(TextReader textReader, ChoXmlRecordConfiguration configuration = null)
        {
            ChoGuard.ArgumentNotNull(textReader, "TextReader");

            Configuration = configuration;
            Init();

            _sr = textReader as StreamReader;
        }

        public ChoXmlReader(XmlReader xmlReader, ChoXmlRecordConfiguration configuration = null)
        {
            ChoGuard.ArgumentNotNull(xmlReader, "XmlReader");

            Configuration = configuration;
            Init();

            _xmlReader = xmlReader;
        }

        public ChoXmlReader(Stream inStream, ChoXmlRecordConfiguration configuration = null)
        {
            ChoGuard.ArgumentNotNull(inStream, "Stream");

            Configuration = configuration;
            Init();

            if (inStream is MemoryStream)
                _sr = new StreamReader(inStream);
            else
                _sr = new StreamReader(inStream, Configuration.GetEncoding(inStream), false, Configuration.BufferSize);
            _closeStreamOnDispose = true;
        }

        public ChoXmlReader(IEnumerable<XElement> xElements, ChoXmlRecordConfiguration configuration = null)
        {
            ChoGuard.ArgumentNotNull(xElements, "XmlElements");

            Configuration = configuration;
            Init();
            _xElements = xElements;
        }

        public ChoXmlReader<T> Load(string filePath)
        {
            ChoGuard.ArgumentNotNullOrEmpty(filePath, "FilePath");

            Close();
            Init();
            _sr = new StreamReader(ChoPath.GetFullPath(filePath), Configuration.GetEncoding(filePath), false, Configuration.BufferSize);
            _xmlReader = XmlReader.Create(_sr,
                new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }, new XmlParserContext(null, Configuration.NamespaceManager, null, XmlSpace.None));
            _closeStreamOnDispose = true;

            return this;
        }

        public ChoXmlReader<T> Load(TextReader textReader)
        {
            ChoGuard.ArgumentNotNull(textReader, "TextReader");

            Close();
            Init();
            _sr = textReader as StreamReader;
            _closeStreamOnDispose = false;

            return this;
        }

        public ChoXmlReader<T> Load(XmlReader xmlReader)
        {
            ChoGuard.ArgumentNotNull(xmlReader, "XmlReader");

            Close();
            Init();
            _xmlReader = xmlReader;
            _closeStreamOnDispose = false;

            return this;
        }

        public ChoXmlReader<T> Load(Stream inStream)
        {
            ChoGuard.ArgumentNotNull(inStream, "Stream");

            Close();
            Init();
            if (inStream is MemoryStream)
                _sr = new StreamReader(inStream);
            else
                _sr = new StreamReader(inStream, Configuration.GetEncoding(inStream), false, Configuration.BufferSize);
            _closeStreamOnDispose = true;

            return this;
        }

        public ChoXmlReader<T> Load(IEnumerable<XElement> xElements)
        {
            ChoGuard.ArgumentNotNull(xElements, "XmlElements");

            Init();
            _xElements = xElements;
            return this;
        }

        public void Close()
        {
            Dispose();
        }

        public T Read()
        {
            if (_enumerator.Value.MoveNext())
                return _enumerator.Value.Current;
            else
                return default(T);
        }

        public void Dispose()
        {
            if (_closeStreamOnDispose)
            {
                if (_xmlReader != null)
                    _xmlReader.Dispose();
                if (_sr != null)
                    _sr.Dispose();
            }

            if (!ChoETLFrxBootstrap.IsSandboxEnvironment)
                System.Threading.Thread.CurrentThread.CurrentCulture = _prevCultureInfo;

            _closeStreamOnDispose = false;
        }

        private void Init()
        {
            _enumerator = new Lazy<IEnumerator<T>>(() => GetEnumerator());
            if (Configuration == null)
                Configuration = new ChoXmlRecordConfiguration(typeof(T));
            else
                Configuration.RecordType = typeof(T);

            Configuration.RecordType = ResolveRecordType(Configuration.RecordType);
            _prevCultureInfo = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = Configuration.Culture;
        }

        public static ChoXmlReader<T> LoadXElements(IEnumerable<XElement> xElements, ChoXmlRecordConfiguration configuration = null)
        {
            var r = new ChoXmlReader<T>(xElements, configuration);
            r._closeStreamOnDispose = true;

            return r;
        }

        public static T LoadXElement(XElement xElement, ChoXmlRecordConfiguration configuration = null)
        {
            if (xElement == null) return default(T);

            return LoadXElements(new XElement[] { xElement }, configuration).FirstOrDefault();
        }

        public static ChoXmlReader<T> LoadText(string inputText, Encoding encoding = null, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            var r = new ChoXmlReader<T>(inputText.ToStream(encoding), configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
            return r;
        }

        public IEnumerable<T> DeserializeText(string inputText, Encoding encoding = null, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(inputText.ToStream(encoding), configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
        }

        public IEnumerable<T> Deserialize(string filePath, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(filePath, configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
        }

        public IEnumerable<T> Deserialize(TextReader textReader, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(textReader, configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
        }

        public IEnumerable<T> Deserialize(Stream inStream, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(inStream, configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
        }

        public IEnumerable<T> Deserialize(IEnumerable<XElement> xElements, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(xElements, configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch };
        }

        public T Deserialize(XElement xElement, ChoXmlRecordConfiguration configuration = null, TraceSwitch traceSwitch = null)
        {
            if (configuration == null)
                configuration = Configuration;

            return new ChoXmlReader<T>(new XElement[] { xElement }, configuration) { TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitch : traceSwitch }.FirstOrDefault();
        }

        //internal static IEnumerator<object> LoadText(Type recType, string inputText, ChoXmlRecordConfiguration configuration, Encoding encoding, int bufferSize, TraceSwitch traceSwitch = null)
        //{
        //    ChoXmlRecordReader rr = new ChoXmlRecordReader(recType, configuration);
        //    rr.TraceSwitch = traceSwitch == null ? ChoETLFramework.TraceSwitchOff : traceSwitch;
        //    return rr.AsEnumerable(new StreamReader(inputText.ToStream(), encoding, false, bufferSize)).GetEnumerator();
        //}

        public IEnumerator<T> GetEnumerator()
        {
            if (_xElements == null)
            {
                ChoXmlRecordReader rr = new ChoXmlRecordReader(typeof(T), Configuration);
                //if (_textReader != null)
                //    _xmlReader = XmlReader.Create(_textReader, new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }, new XmlParserContext(null, Configuration.NamespaceManager, null, XmlSpace.None));

                rr.Reader = this;
                rr.TraceSwitch = TraceSwitch;
                rr.RowsLoaded += NotifyRowsLoaded;
                rr.MembersDiscovered += MembersDiscovered;
                var e = rr.AsEnumerable(_xmlReader).GetEnumerator();
                return ChoEnumeratorWrapper.BuildEnumerable<T>(() => e.MoveNext(), () => (T)ChoConvert.ChangeType<ChoRecordFieldAttribute>(e.Current, typeof(T))).GetEnumerator();
            }
            else
            {
                ChoXmlRecordReader rr = new ChoXmlRecordReader(typeof(T), Configuration);

                rr.Reader = this;
                rr.TraceSwitch = TraceSwitch;
                rr.RowsLoaded += NotifyRowsLoaded;
                rr.MembersDiscovered += MembersDiscovered;
                var e = rr.AsEnumerable(_xElements).GetEnumerator();
                return ChoEnumeratorWrapper.BuildEnumerable<T>(() => e.MoveNext(), () => (T)ChoConvert.ChangeType<ChoRecordFieldAttribute>(e.Current, typeof(T))).GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IDataReader AsDataReader()
        {
            if (_xElements == null)
            {
                ChoXmlRecordReader rr = new ChoXmlRecordReader(typeof(T), Configuration);
                //if (_textReader != null)
                //    _xmlReader = XmlReader.Create(_textReader, new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }, new XmlParserContext(null, Configuration.NamespaceManager, null, XmlSpace.None));
                rr.Reader = this;
                rr.TraceSwitch = TraceSwitch;
                rr.RowsLoaded += NotifyRowsLoaded;
                rr.MembersDiscovered += MembersDiscovered;
                var dr = new ChoEnumerableDataReader(rr.AsEnumerable(_xmlReader), rr);
                return dr;
            }
            else
            {
                ChoXmlRecordReader rr = new ChoXmlRecordReader(typeof(T), Configuration);

                rr.Reader = this;
                rr.TraceSwitch = TraceSwitch;
                rr.RowsLoaded += NotifyRowsLoaded;
                rr.MembersDiscovered += MembersDiscovered;
                var dr = new ChoEnumerableDataReader(rr.AsEnumerable(_xElements), rr);
                return dr;
            }
        }

        public DataTable AsDataTable(string tableName = null)
        {
            DataTable dt = tableName.IsNullOrWhiteSpace() ? new DataTable() : new DataTable(tableName);
            dt.Load(AsDataReader());
            return dt;
        }

        public int Fill(DataTable dt)
        {
            if (dt == null)
                throw new ArgumentException("Missing datatable.");
            dt.Load(AsDataReader());

            return dt.Rows.Count;
        }

        private void NotifyRowsLoaded(object sender, ChoRowsLoadedEventArgs e)
        {
            EventHandler<ChoRowsLoadedEventArgs> rowsLoadedEvent = RowsLoaded;
            if (rowsLoadedEvent == null)
            {
                if (!e.IsFinal)
                    ChoETLLog.Info(e.RowsLoaded.ToString("#,##0") + " records loaded.");
                else
                    ChoETLLog.Info("Total " + e.RowsLoaded.ToString("#,##0") + " records loaded.");
            }
            else
                rowsLoadedEvent(this, e);
        }

        #region Fluent API

        public ChoXmlReader<T> NotifyAfter(long rowsLoaded)
        {
            Configuration.NotifyAfter = rowsLoaded;
            return this;
        }

        public ChoXmlReader<T> WithXmlNamespaceManager(XmlNamespaceManager nsMgr)
        {
            ChoGuard.ArgumentNotNull(nsMgr, "XmlNamespaceManager");

            Configuration.NamespaceManager = nsMgr;
            return this;
        }

        public ChoXmlReader<T> WithXmlNamespace(string prefix, string uri)
        {
            Configuration.NamespaceManager.AddNamespace(prefix, uri);

            return this;
        }

        public ChoXmlReader<T> WithXPath(string xPath)
        {
            Configuration.XPath = xPath;
            return this;
        }

        public ChoXmlReader<T> UseXmlSerialization()
        {
            Configuration.UseXmlSerialization = true;
            return this;
        }

        public ChoXmlReader<T> IgnoreField(string fieldName)
        {
            if (!fieldName.IsNullOrWhiteSpace())
            {
                string fnTrim = null;
                if (!_clearFields)
                {
                    Configuration.XmlRecordFieldConfigurations.Clear();
                    _clearFields = true;
                    Configuration.MapRecordFields(Configuration.RecordType);
                }
                fnTrim = fieldName.NTrim();
                if (Configuration.XmlRecordFieldConfigurations.Any(o => o.Name == fnTrim))
                    Configuration.XmlRecordFieldConfigurations.Remove(Configuration.XmlRecordFieldConfigurations.Where(o => o.Name == fnTrim).First());
            }

            return this;
        }

        public ChoXmlReader<T> WithFields(params string[] fieldsNames)
        {
            string fnTrim = null;
            if (!fieldsNames.IsNullOrEmpty())
            {
                foreach (string fn in fieldsNames)
                {
                    if (fn.IsNullOrEmpty())
                        continue;
                    if (!_clearFields)
                    {
                        Configuration.XmlRecordFieldConfigurations.Clear();
                        _clearFields = true;
                        Configuration.MapRecordFields(Configuration.RecordType);
                    }
                    fnTrim = fn.NTrim();
                    if (Configuration.XmlRecordFieldConfigurations.Any(o => o.Name == fnTrim))
                        Configuration.XmlRecordFieldConfigurations.Remove(Configuration.XmlRecordFieldConfigurations.Where(o => o.Name == fnTrim).First());

                    Configuration.XmlRecordFieldConfigurations.Add(new ChoXmlRecordFieldConfiguration(fnTrim, $"//{fnTrim}"));
                }

            }

            return this;
        }

        public ChoXmlReader<T> WithXmlElementField(string name, Type fieldType = null, ChoFieldValueTrimOption fieldValueTrimOption = ChoFieldValueTrimOption.Trim, string fieldName = null, 
            Func<object, object> valueConverter = null,
            Func<object, object> itemConverter = null,
            object defaultValue = null, object fallbackValue = null, bool encodeValue = false)
        {
            string fnTrim = name.NTrim();
            string xPath = $"//{fnTrim}";
            return WithField(fnTrim, xPath, fieldType, fieldValueTrimOption, false, fieldName, false, valueConverter, itemConverter, defaultValue, fallbackValue, encodeValue);
        }

        public ChoXmlReader<T> WithXmlAttributeField(string name, Type fieldType = null, ChoFieldValueTrimOption fieldValueTrimOption = ChoFieldValueTrimOption.Trim, string fieldName = null, 
            Func<object, object> valueConverter = null,
            Func<object, object> itemConverter = null,
            object defaultValue = null, object fallbackValue = null, bool encodeValue = false)
        {
            string fnTrim = name.NTrim();
            string xPath = $"//@{fnTrim}";
            return WithField(fnTrim, xPath, fieldType, fieldValueTrimOption, true, fieldName, false, valueConverter, itemConverter, defaultValue, fallbackValue, encodeValue);
        }

        public ChoXmlReader<T> WithField(string name, string xPath = null, Type fieldType = null, ChoFieldValueTrimOption fieldValueTrimOption = ChoFieldValueTrimOption.Trim, bool isXmlAttribute = false, string fieldName = null, bool isArray = false, 
            Func<object, object> valueConverter = null,
            Func<object, object> itemConverter = null,
            object defaultValue = null, object fallbackValue = null,
            bool encodeValue = false)
        {
            if (!name.IsNullOrEmpty())
            {
                if (!_clearFields)
                {
                    Configuration.XmlRecordFieldConfigurations.Clear();
                    _clearFields = true;
                    Configuration.MapRecordFields(Configuration.RecordType);
                }

                string fnTrim = name.NTrim();
                xPath = xPath.IsNullOrWhiteSpace() ? $"//{fnTrim}" : xPath;

                if (Configuration.XmlRecordFieldConfigurations.Any(o => o.Name == fnTrim))
                    Configuration.XmlRecordFieldConfigurations.Remove(Configuration.XmlRecordFieldConfigurations.Where(o => o.Name == fnTrim).First());

                Configuration.XmlRecordFieldConfigurations.Add(new ChoXmlRecordFieldConfiguration(fnTrim, xPath) { FieldType = fieldType,
                    FieldValueTrimOption = fieldValueTrimOption, IsXmlAttribute = isXmlAttribute, FieldName = fieldName, IsArray = isArray,
                    ValueConverter = valueConverter,
                    ItemConverter = itemConverter,
                    DefaultValue = defaultValue,
                    FallbackValue = fallbackValue,
                    EncodeValue = encodeValue
                });
            }

            return this;
        }

        public ChoXmlReader<T> ColumnCountStrict()
        {
            Configuration.ColumnCountStrict = true;
            return this;
        }

        public ChoXmlReader<T> Configure(Action<ChoXmlRecordConfiguration> action)
        {
            if (action != null)
                action(Configuration);

            return this;
        }
        public ChoXmlReader<T> Setup(Action<ChoXmlReader<T>> action)
        {
            if (action != null)
                action(this);

            return this;
        }

        public ChoXmlReader<T> MapRecordFields<T1>()
        {
            Configuration.MapRecordFields<T1>();
            return this;
        }

        public ChoXmlReader<T> MapRecordFields(Type recordType)
        {
            if (recordType != null)
                Configuration.MapRecordFields(recordType);

            return this;
        }

        #endregion Fluent API
    }

    public class ChoXmlReader : ChoXmlReader<dynamic>
    {
        public ChoXmlReader(string filePath, string defaultNamespace)
            : base(filePath, defaultNamespace)
        {

        }

        public ChoXmlReader(string filePath, ChoXmlRecordConfiguration configuration = null)
            : base(filePath, configuration)
        {

        }
        public ChoXmlReader(TextReader txtReader, ChoXmlRecordConfiguration configuration = null)
            : base(txtReader, configuration)
        {
        }
        public ChoXmlReader(XmlReader xmlReader, ChoXmlRecordConfiguration configuration = null)
            : base(xmlReader, configuration)
        {
        }
        public ChoXmlReader(Stream inStream, ChoXmlRecordConfiguration configuration = null)
            : base(inStream, configuration)
        {
        }
    }
}
