using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if _ALL_NET_
using System.Windows.Data;
#endif

namespace ChoETL
{
    [ChoTypeConverter(typeof(Boolean))]
#if _ALL_NET_
    public class ChoBooleanConverter : IValueConverter
#else
    public class ChoBooleanConverter : IChoValueConverter
#endif
    {
        public ChoBooleanConverter()
        {

        }
        private ChoBooleanFormatSpec GetTypeFormat(object parameter, out string customBoolFormatText)
        {
            customBoolFormatText = null;
            var typeFormat = parameter.GetValueAt<object>(0);

            if (typeFormat is ChoTypeConverterFormatSpec ts)
            {
                customBoolFormatText = ts.CustomBooleanFormatText;
                return ts.BooleanFormat;
            }
            else if (typeFormat is string tf)
                return ChoBooleanFormatSpec.Custom;

            return parameter.GetValueAt(0, ChoTypeConverterFormatSpec.Instance.BooleanFormat);
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string)
            {
                string txt = value as string;
                txt = txt.NTrim();

                if (txt.IsNull())
                    throw new ChoParserException($"Invalid data `{txt}` found.");

                string customBoolFormatText = null;
                ChoBooleanFormatSpec booleanFormat = GetTypeFormat(parameter, out customBoolFormatText); //.GetValueAt(0, ChoTypeConverterFormatSpec.Instance.BooleanFormat);

                // The configured format is tried first (it also controls how values are written).
                // When reading, the other standard tokens (1/0, Y/N, T/F, true/false, yes/no) are
                // accepted as well, so e.g. "0"/"1" columns parse with the default TrueOrFalse
                // format (#219). Text that matches nothing still raises an error.
                bool result;
                switch (booleanFormat)
                {
                    case ChoBooleanFormatSpec.YOrN:
                        if (TryParseSingleChar(txt, 'Y', 'N', out result))
                            return result;
                        break;
                    case ChoBooleanFormatSpec.TOrF:
                        if (TryParseSingleChar(txt, 'T', 'F', out result))
                            return result;
                        break;
                    case ChoBooleanFormatSpec.TrueOrFalse:
                        if (TryParseWord(txt, "true", "false", out result))
                            return result;
                        break;
                    case ChoBooleanFormatSpec.YesOrNo:
                        if (TryParseWord(txt, "yes", "no", out result))
                            return result;
                        break;
                    case ChoBooleanFormatSpec.ZeroOrOne:
                        if (TryParseSingleChar(txt, '1', '0', out result))
                            return result;
                        break;
                    default:
                        string boolTxt = parameter.GetValueAt<string>(0);
                        if (boolTxt.IsNullOrWhiteSpace())
                            boolTxt = customBoolFormatText;

                        string trueBoolTxt = boolTxt.SplitNTrim().FirstOrDefault();
                        string falseBoolTxt = boolTxt.SplitNTrim().Skip(1).FirstOrDefault();
                        if (!trueBoolTxt.IsNullOrWhiteSpace()
                            && !falseBoolTxt.IsNullOrWhiteSpace()
                            && TryParseWord(txt, trueBoolTxt, falseBoolTxt, out result))
                            return result;
                        break;
                }

                if (TryParseAny(txt, out result))
                    return result;

                throw new ChoParserException($"Invalid data `{txt}` found.");
            }
            else
                return value;
        }

        private static bool TryParseSingleChar(string txt, char trueChar, char falseChar, out bool result)
        {
            result = false;
            if (txt.Length != 1)
                return false;

            char c = char.ToUpperInvariant(txt[0]);
            if (c == char.ToUpperInvariant(trueChar))
            {
                result = true;
                return true;
            }
            if (c == char.ToUpperInvariant(falseChar))
                return true;

            return false;
        }

        private static bool TryParseWord(string txt, string trueText, string falseText, out bool result)
        {
            result = false;
            if (String.Compare(txt, trueText, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = true;
                return true;
            }
            return String.Compare(txt, falseText, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool TryParseAny(string txt, out bool result)
        {
            return TryParseSingleChar(txt, '1', '0', out result)
                || TryParseSingleChar(txt, 'Y', 'N', out result)
                || TryParseSingleChar(txt, 'T', 'F', out result)
                || TryParseWord(txt, "true", "false", out result)
                || TryParseWord(txt, "yes", "no", out result);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (targetType == typeof(string))
            {
                if (value == null)
                    value = false;

                if (value is bool)
                {
                    bool boolValue = (bool)value;

                    string customBoolFormatText = null;
                    ChoBooleanFormatSpec booleanFormat = GetTypeFormat(parameter, out customBoolFormatText); //.GetValueAt(0, ChoTypeConverterFormatSpec.Instance.BooleanFormat);
                    switch (booleanFormat)
                    {
                        case ChoBooleanFormatSpec.TOrF:
                            return boolValue ? "T" : "F";
                        case ChoBooleanFormatSpec.YOrN:
                            return boolValue ? "Y" : "N";
                        case ChoBooleanFormatSpec.TrueOrFalse:
                            return boolValue ? "True" : "False";
                        case ChoBooleanFormatSpec.YesOrNo:
                            return boolValue ? "Yes" : "No";
                        default:
                            if (customBoolFormatText == null)
                                return boolValue ? "1" : "0";
                            else
                            {
                                string boolTxt = parameter.GetValueAt<string>(0);
                                if (boolTxt.IsNullOrWhiteSpace())
                                    boolTxt = customBoolFormatText;

                                string trueBoolTxt = boolTxt.SplitNTrim().FirstOrDefault();
                                string falseBoolTxt = boolTxt.SplitNTrim().Skip(1).FirstOrDefault();

                                return boolValue ? trueBoolTxt : falseBoolTxt;
                            }
                    }
                }
            }
            else if (value == DBNull.Value)
                return null;

            return value;
        }
    }
}
