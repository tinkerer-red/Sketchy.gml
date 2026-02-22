using System.Text;

namespace Sketchy
{
	internal enum GmlLexMode
	{
		Code,
		LineComment,
		BlockComment,
		StringEsc,
		StringRawDouble,
		StringRawSingle,
		TemplateText,
		TemplateExpr,
	}

	internal sealed class GmlLexer
	{
		public static bool IsIdentifierStart(char _char)
		{
			return _char == '_' || char.IsLetter(_char);
		}

		public static bool IsIdentifierPart(char _char)
		{
			return _char == '_' || char.IsLetterOrDigit(_char);
		}

		public static bool IsWhitespace(char _char)
		{
			return _char == ' ' || _char == '\t' || _char == '\r' || _char == '\n';
		}

		public static int SkipWhitespace(string _text, int _index)
		{
			while (_index < _text.Length && IsWhitespace(_text[_index]))
			{
				_index++;
			}
			return _index;
		}

		public static int SkipWhitespaceAndComments(string _text, int _index, ref bool _in_block_comment)
		{
			while (_index < _text.Length)
			{
				_index = SkipWhitespace(_text, _index);
				if (_index >= _text.Length)
				{
					return _index;
				}

				if (_in_block_comment)
				{
					int _close = _text.IndexOf("*/", _index, StringComparison.Ordinal);
					if (_close < 0)
					{
						return _text.Length;
					}
					_in_block_comment = false;
					_index = _close + 2;
					continue;
				}

				if (_index + 1 < _text.Length)
				{
					char _a = _text[_index];
					char _b = _text[_index + 1];
					if (_a == '/' && _b == '/')
					{
						return _text.Length;
					}
					if (_a == '/' && _b == '*')
					{
						_in_block_comment = true;
						_index += 2;
						continue;
					}
				}

				return _index;
			}

			return _index;
		}

		public static bool EndsWithContinuationBackslash(string _line, bool _starting_block_comment)
		{
			GmlLexMode _mode = _starting_block_comment ? GmlLexMode.BlockComment : GmlLexMode.Code;
			bool _escape = false;
			for (int _index = 0; _index < _line.Length; _index++)
			{
				char _char = _line[_index];
				char _next = _index + 1 < _line.Length ? _line[_index + 1] : '\0';

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						_index = _line.Length;
						break;

					case GmlLexMode.BlockComment:
						if (_char == '*' && _next == '/')
						{
							_mode = GmlLexMode.Code;
							_index++;
						}
						break;

					case GmlLexMode.StringEsc:
						if (_escape)
						{
							_escape = false;
						}
						else if (_char == '\\')
						{
							_escape = true;
						}
						else if (_char == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.StringRawDouble:
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.StringRawSingle:
						if (_char == '\'')
						{
							_mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.TemplateText:
						if (_char == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
						}
						else if (_char == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.TemplateExpr:
						// For continuation checks, treating template expr as code is enough.
						goto case GmlLexMode.Code;

					case GmlLexMode.Code:
						if (_char == '/' && _next == '/')
						{
							_mode = GmlLexMode.LineComment;
							_index++;
							break;
						}
						if (_char == '/' && _next == '*')
						{
							_mode = GmlLexMode.BlockComment;
							_index++;
							break;
						}
						if (_char == '@' && _next == '"')
						{
							_mode = GmlLexMode.StringRawDouble;
							_index++;
							break;
						}
						if (_char == '@' && _next == '\'')
						{
							_mode = GmlLexMode.StringRawSingle;
							_index++;
							break;
						}
						if (_char == '$' && _next == '"')
						{
							_mode = GmlLexMode.TemplateText;
							_index++;
							break;
						}
						if (_char == '"')
						{
							_mode = GmlLexMode.StringEsc;
							_escape = false;
							break;
						}
						break;
				}
			}

			// Find last non-whitespace character and verify we are in code mode at end.
			int _last = _line.Length - 1;
			while (_last >= 0 && (_line[_last] == ' ' || _line[_last] == '\t' || _line[_last] == '\r'))
			{
				_last--;
			}

			if (_last < 0)
			{
				return false;
			}

			if (_line[_last] != '\\')
			{
				return false;
			}

			return _mode == GmlLexMode.Code;
		}

		public static bool TryReadIdentifier(string _text, ref int _index, out string _value)
		{
			_value = string.Empty;
			if (_index >= _text.Length)
			{
				return false;
			}

			char _char = _text[_index];
			if (!IsIdentifierStart(_char))
			{
				return false;
			}

			int _start = _index;
			_index++;
			while (_index < _text.Length && IsIdentifierPart(_text[_index]))
			{
				_index++;
			}

			_value = _text.Substring(_start, _index - _start);
			return true;
		}

		public static string JoinLinesSingleLine(IEnumerable<string> _lines)
		{
			StringBuilder _builder = new StringBuilder();
			bool _first = true;
			foreach (string _line in _lines)
			{
				string _trim = _line.Trim();
				if (_trim.Length == 0)
				{
					continue;
				}
				if (!_first)
				{
					_builder.Append(' ');
				}
				_first = false;
				_builder.Append(_trim);
			}
			return _builder.ToString();
		}
	}
}
