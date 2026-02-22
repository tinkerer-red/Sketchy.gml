using System.Globalization;

namespace Sketchy
{
	internal enum TinyValueKind
	{
		None,
		Number,
		String,
	}

	internal readonly struct TinyValue
	{
		private readonly string? _string_value;

		public TinyValueKind Kind { get; }
		public double NumberValue { get; }
		public string StringValue => _string_value ?? string.Empty;

		public TinyValue(double _number)
		{
			Kind = TinyValueKind.Number;
			NumberValue = _number;
			_string_value = string.Empty;
		}

		public TinyValue(string _string)
		{
			Kind = TinyValueKind.String;
			NumberValue = 0.0;
			_string_value = _string;
		}

		public static TinyValue None => default;
	}

	internal sealed class TinyEvaluator
	{
		private readonly string _text;
		private int _index;

		public TinyEvaluator(string _text)
		{
			this._text = _text;
			_index = 0;
		}

		public static bool TryEvaluate(string _text, out string _literal_text)
		{
			_literal_text = string.Empty;
			TinyEvaluator _eval = new TinyEvaluator(_text);
			TinyValue _value;
			if (!_eval.TryParseExpression(out _value))
			{
				return false;
			}
			_eval.SkipSpaces();
			if (_eval._index != _eval._text.Length)
			{
				return false;
			}

			switch (_value.Kind)
			{
				case TinyValueKind.Number:
					_literal_text = _value.NumberValue.ToString("G17", CultureInfo.InvariantCulture);
					return true;
				case TinyValueKind.String:
					// Output as escaped normal string literal.
					_literal_text = "\"" + _value.StringValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
					return true;
				default:
					return false;
			}
		}

		private void SkipSpaces()
		{
			while (_index < _text.Length)
			{
				char _char = _text[_index];
				if (_char == ' ' || _char == '\t' || _char == '\r' || _char == '\n')
				{
					_index++;
					continue;
				}
				break;
			}
		}

		private bool TryParseExpression(out TinyValue _value)
		{
			if (!TryParseTerm(out _value))
			{
				return false;
			}

			while (true)
			{
				SkipSpaces();
				if (_index >= _text.Length)
				{
					return true;
				}
				char _op = _text[_index];
				if (_op != '+' && _op != '-')
				{
					return true;
				}
				_index++;
				if (!TryParseTerm(out TinyValue _rhs))
				{
					return false;
				}

				if (!TryApplyBinary(_value, _rhs, _op, out _value))
				{
					return false;
				}
			}
		}

		private bool TryParseTerm(out TinyValue _value)
		{
			if (!TryParseFactor(out _value))
			{
				return false;
			}

			while (true)
			{
				SkipSpaces();
				if (_index >= _text.Length)
				{
					return true;
				}
				char _op = _text[_index];
				if (_op != '*' && _op != '/')
				{
					return true;
				}
				_index++;
				if (!TryParseFactor(out TinyValue _rhs))
				{
					return false;
				}

				if (!TryApplyBinary(_value, _rhs, _op, out _value))
				{
					return false;
				}
			}
		}

		private bool TryParseFactor(out TinyValue _value)
		{
			SkipSpaces();
			_value = TinyValue.None;
			if (_index >= _text.Length)
			{
				return false;
			}

			char _char = _text[_index];
			if (_char == '(')
			{
				_index++;
				if (!TryParseExpression(out _value))
				{
					return false;
				}
				SkipSpaces();
				if (_index >= _text.Length || _text[_index] != ')')
				{
					return false;
				}
				_index++;
				return true;
			}

			if (_char == '+' || _char == '-')
			{
				_index++;
				if (!TryParseFactor(out TinyValue _inner))
				{
					return false;
				}
				if (_inner.Kind != TinyValueKind.Number)
				{
					return false;
				}
				double _num = _inner.NumberValue;
				if (_char == '-')
				{
					_num = -_num;
				}
				_value = new TinyValue(_num);
				return true;
			}

			if (_char == '"')
			{
				if (!TryParseEscapedString(out string _string))
				{
					return false;
				}
				_value = new TinyValue(_string);
				return true;
			}

			if (IsNumberStart(_char))
			{
				if (!TryParseNumber(out double _number))
				{
					return false;
				}
				_value = new TinyValue(_number);
				return true;
			}

			return false;
		}

		private static bool TryApplyBinary(TinyValue _lhs, TinyValue _rhs, char _op, out TinyValue _result)
		{
			_result = TinyValue.None;

			if (_lhs.Kind == TinyValueKind.Number && _rhs.Kind == TinyValueKind.Number)
			{
				double _a = _lhs.NumberValue;
				double _b = _rhs.NumberValue;
				switch (_op)
				{
					case '+':
						_result = new TinyValue(_a + _b);
						return true;
					case '-':
						_result = new TinyValue(_a - _b);
						return true;
					case '*':
						_result = new TinyValue(_a * _b);
						return true;
					case '/':
						_result = new TinyValue(_a / _b);
						return true;
					default:
						return false;
				}
			}

			if (_op == '+' && _lhs.Kind == TinyValueKind.String && _rhs.Kind == TinyValueKind.String)
			{
				_result = new TinyValue(_lhs.StringValue + _rhs.StringValue);
				return true;
			}

			if (_op == '+' && _lhs.Kind == TinyValueKind.String && _rhs.Kind == TinyValueKind.Number)
			{
				_result = new TinyValue(_lhs.StringValue + _rhs.NumberValue.ToString("G17", CultureInfo.InvariantCulture));
				return true;
			}

			if (_op == '+' && _lhs.Kind == TinyValueKind.Number && _rhs.Kind == TinyValueKind.String)
			{
				_result = new TinyValue(_lhs.NumberValue.ToString("G17", CultureInfo.InvariantCulture) + _rhs.StringValue);
				return true;
			}

			return false;
		}

		private static bool IsNumberStart(char _char)
		{
			return (_char >= '0' && _char <= '9') || _char == '.';
		}

		private bool TryParseNumber(out double _number)
		{
			_number = 0.0;
			int _start = _index;
			bool _has_dot = false;

			while (_index < _text.Length)
			{
				char _char = _text[_index];
				if (_char >= '0' && _char <= '9')
				{
					_index++;
					continue;
				}
				if (_char == '.' && !_has_dot)
				{
					_has_dot = true;
					_index++;
					continue;
				}
				break;
			}

			if (_index == _start)
			{
				return false;
			}

			string _slice = _text.Substring(_start, _index - _start);
			return double.TryParse(_slice, NumberStyles.Float, CultureInfo.InvariantCulture, out _number);
		}

		private bool TryParseEscapedString(out string _value)
		{
			_value = string.Empty;
			if (_index >= _text.Length || _text[_index] != '"')
			{
				return false;
			}
			_index++;

			System.Text.StringBuilder _builder = new System.Text.StringBuilder();
			while (_index < _text.Length)
			{
				char _char = _text[_index++];
				if (_char == '"')
				{
					_value = _builder.ToString();
					return true;
				}
				if (_char == '\\')
				{
					if (_index >= _text.Length)
					{
						return false;
					}
					char _esc = _text[_index++];
					switch (_esc)
					{
						case 'n':
							_builder.Append('\n');
							break;
						case 'r':
							_builder.Append('\r');
							break;
						case 't':
							_builder.Append('\t');
							break;
						case '\\':
							_builder.Append('\\');
							break;
						case '"':
							_builder.Append('"');
							break;
						default:
							_builder.Append(_esc);
							break;
					}
					continue;
				}
				_builder.Append(_char);
			}
			return false;
		}
	}
}