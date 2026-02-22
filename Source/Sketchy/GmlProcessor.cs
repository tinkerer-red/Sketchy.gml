using System.Security.Cryptography;
using System.Text;

namespace Sketchy
{
	internal sealed class GmlProcessor
	{
		private readonly MacroTable _macro_table;

		public GmlProcessor(MacroTable _macro_table)
		{
			this._macro_table = _macro_table;
		}

		public static string ComputeHashText(string _text)
		{
			byte[] _bytes = Encoding.UTF8.GetBytes(_text);
			byte[] _hash = SHA256.HashData(_bytes);
			return Convert.ToHexString(_hash);
		}

		public static bool FileContainsDirectivesOrMacroRefs(string _text, IReadOnlyDictionary<string, MacroDefinition> _macros_by_name)
		{
			bool _has_directive = false;
			bool _in_block_comment = false;
			string[] _lines = _text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
			for (int _line_index = 0; _line_index < _lines.Length; _line_index++)
			{
				string _line = _lines[_line_index];
				int _idx = 0;
				_idx = GmlLexer.SkipWhitespace(_line, _idx);
				if (_idx >= _line.Length)
				{
					continue;
				}

				if (_in_block_comment)
				{
					int _close = _line.IndexOf("*/", _idx, StringComparison.Ordinal);
					if (_close >= 0)
					{
						_in_block_comment = false;
					}
					continue;
				}

				if (_idx + 1 < _line.Length && _line[_idx] == '/' && _line[_idx + 1] == '*')
				{
					_in_block_comment = true;
					continue;
				}

				if (_line.AsSpan(_idx).StartsWith("#macro", StringComparison.Ordinal))
				{
					_has_directive = true;
					break;
				}

				if (_line.AsSpan(_idx).StartsWith("const", StringComparison.Ordinal))
				{
					// anchored const directive
					_has_directive = true;
					break;
				}
			}

			if (_has_directive)
			{
				return true;
			}

			// Macro reference scan (token-lite)
			if (_macros_by_name.Count == 0)
			{
				return false;
			}

			HashSet<string> _macro_names = new HashSet<string>(_macros_by_name.Keys, StringComparer.Ordinal);
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;
			int _length = _text.Length;
			char _prev_non_ws = '\0';

			for (int _index = 0; _index < _length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _length ? _text[_index + 1] : '\0';

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						if (_char == '\n')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;

					case GmlLexMode.BlockComment:
						if (_char == '*' && _next == '/')
						{
							_mode = GmlLexMode.Code;
							_index++;
							_prev_non_ws = '\0';
						}
						continue;

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
							_prev_non_ws = '\0';
						}
						continue;

					case GmlLexMode.StringRawDouble:
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;

					case GmlLexMode.StringRawSingle:
						if (_char == '\'')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;

					case GmlLexMode.TemplateText:
						if (_char == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
							_prev_non_ws = '{';
							continue;
						}
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
							continue;
						}
						continue;

					case GmlLexMode.TemplateExpr:
						// treat like code
						goto case GmlLexMode.Code;

					case GmlLexMode.Code:
						if (_char == '/' && _next == '/')
						{
							_mode = GmlLexMode.LineComment;
							_index++;
							continue;
						}
						if (_char == '/' && _next == '*')
						{
							_mode = GmlLexMode.BlockComment;
							_index++;
							continue;
						}
						if (_char == '@' && _next == '"')
						{
							_mode = GmlLexMode.StringRawDouble;
							_index++;
							continue;
						}
						if (_char == '@' && _next == '\'')
						{
							_mode = GmlLexMode.StringRawSingle;
							_index++;
							continue;
						}
						if (_char == '$' && _next == '"')
						{
							_mode = GmlLexMode.TemplateText;
							_index++;
							continue;
						}
						if (_char == '"')
						{
							_mode = GmlLexMode.StringEsc;
							_escape = false;
							continue;
						}

						if (_char == ' ' || _char == '\t' || _char == '\r' || _char == '\n')
						{
							continue;
						}

						if (GmlLexer.IsIdentifierStart(_char))
						{
							int _start = _index;
							int _scan = _index + 1;
							while (_scan < _length && GmlLexer.IsIdentifierPart(_text[_scan]))
							{
								_scan++;
							}
							string _ident = _text.Substring(_start, _scan - _start);
							_index = _scan - 1;

							if (_prev_non_ws != '.' && _macro_names.Contains(_ident))
							{
								return true;
							}

							_prev_non_ws = 'a';
							continue;
						}

						_prev_non_ws = _char;
						break;
				}
			}

			return false;
		}

		public ProcessResult PreprocessText(string _file_path, string _text)
		{
			GmlFileRewriter _rewriter = new GmlFileRewriter(_macro_table);
			return _rewriter.Rewrite(_file_path, _text);
		}
	}

	internal sealed class ProcessResult
	{
		public bool DidModify { get; }
		public string OutputText { get; }

		public ProcessResult(bool _did_modify, string _output_text)
		{
			DidModify = _did_modify;
			OutputText = _output_text;
		}
	}
}
