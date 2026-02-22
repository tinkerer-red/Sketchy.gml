using System.Text;

namespace Sketchy
{
	internal enum ConstPlanKind
	{
		Inline,
		Static,
		Alias,
	}

	internal sealed class ConstInfo
	{
		public string Name { get; }
		public ConstPlanKind PlanKind { get; set; }
		public string ValueText { get; set; }
		public int LineIndex { get; }

		public ConstInfo(string _name, int _line_index, ConstPlanKind _plan_kind, string _value_text)
		{
			Name = _name;
			LineIndex = _line_index;
			PlanKind = _plan_kind;
			ValueText = _value_text;
		}
	}

	internal sealed class ScopeFrame
	{
		public Dictionary<string, ConstInfo> ConstsByName { get; } = new Dictionary<string, ConstInfo>(StringComparer.Ordinal);
		public HashSet<int> ConstLineIndices { get; } = new HashSet<int>();
	}

	internal sealed class GmlFileRewriter
	{
		private readonly MacroTable _macro_table;

		public GmlFileRewriter(MacroTable _macro_table)
		{
			this._macro_table = _macro_table;
		}

		public ProcessResult Rewrite(string _file_path, string _text)
		{
			string _normalized = _text.Replace("\r\n", "\n").Replace("\r", "\n");
			string[] _lines = _normalized.Split('\n');
			
			HashSet<int> _macro_def_lines = CollectMacroDefinitionLines(_file_path);

			// First pass: parse scopes + const definitions.
			List<ScopeFrame> _all_scopes = new List<ScopeFrame>();
			ScopeFrame _root_scope = new ScopeFrame();
			_all_scopes.Add(_root_scope);

			Dictionary<int, int> _line_to_scope_index = BuildScopesAndCollectConsts(_file_path, _normalized, _lines, _root_scope, _all_scopes);

			// Resolve consts per scope (in order of appearance within that scope).
			ResolveConsts(_file_path, _lines, _all_scopes);

			// Second pass: build output with line-stable directive rewrites.
			bool _did_modify = false;
			string[] _out_lines = new string[_lines.Length];
			for (int _line_index = 0; _line_index < _lines.Length; _line_index++)
			{
				string _line = _lines[_line_index];

				if (_macro_def_lines.Contains(_line_index))
				{
					_out_lines[_line_index] = "//" + _line;
					_did_modify = true;
					continue;
				}

				int _scope_index = _line_to_scope_index.TryGetValue(_line_index, out int _value_scope) ? _value_scope : 0;
				ScopeFrame _scope = _all_scopes[_scope_index];

				if (_scope.ConstLineIndices.Contains(_line_index))
				{
					// Find const info by line
					ConstInfo? _info = _scope.ConstsByName.Values.FirstOrDefault(v => v.LineIndex == _line_index);
					if (_info == null)
					{
						_out_lines[_line_index] = "//" + _line;
						_did_modify = true;
						continue;
					}

					if (_info.PlanKind == ConstPlanKind.Static)
					{
						bool _is_root_scope = (_scope_index == 0);
						_out_lines[_line_index] = BuildStaticConstLine(_line, _info, _is_root_scope);
						_did_modify = true;
						continue;
					}

					_out_lines[_line_index] = "//" + _line;
					_did_modify = true;
					continue;
				}

				// Non-directive lines: expand macros + const replacements with scope stack.
				string _expanded = ExpandLineWithScope(_line, _scope, _all_scopes[0]);
				_out_lines[_line_index] = _expanded;
				if (!ReferenceEquals(_expanded, _line) && _expanded != _line)
				{
					_did_modify = true;
				}
			}

			string _out_text = string.Join("\n", _out_lines);
			return new ProcessResult(_did_modify, _out_text);
		}

		private HashSet<int> CollectMacroDefinitionLines(string _file_path)
		{
			HashSet<int> _set = new HashSet<int>();
			foreach (MacroDefinition _macro in _macro_table.MacrosByName.Values)
			{
				if (!string.Equals(_macro.FilePath, _file_path, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				for (int _line = _macro.LineStart; _line <= _macro.LineEnd; _line++)
				{
					_set.Add(_line);
				}
			}
			return _set;
		}

		private static string BuildStaticConstLine(string _original_line, ConstInfo _info, bool _is_root_scope)
		{
			// Preserve whether the author ended the original line with a semicolon.
			string _trimmed = _original_line.TrimEnd();
			bool _has_semicolon = (_trimmed.Length > 0 && _trimmed[_trimmed.Length - 1] == ';');

			string _rhs = _info.ValueText.Trim();
			string _name = _info.Name;

			if (_is_root_scope)
			{
				// Script scope: wrap in IIFE to own the static, since static is illegal at script scope.
				// Parentheses around the function declaration are required.
				string _line = $"var {_name} = (function(){{static __ = {_rhs}; return __;}})()";
				if (_has_semicolon)
				{
					_line += ";";
				}
				return _line;
			}

			// Function scope: static is legal. Use static backing and var alias.
			string _static_name = $"__const_{_name}";
			string _out = $"static {_static_name} = {_rhs}; var {_name} = {_static_name}";
			if (_has_semicolon)
			{
				_out += ";";
			}
			return _out;
		}

		private static string ReplaceConstKeywordWithStatic(string _line)
		{
			int _idx = 0;
			while (_idx < _line.Length && (_line[_idx] == ' ' || _line[_idx] == '\t'))
			{
				_idx++;
			}
			if (_line.AsSpan(_idx).StartsWith("const", StringComparison.Ordinal))
			{
				return _line.Substring(0, _idx) + "static" + _line.Substring(_idx + 5);
			}
			return _line;
		}

		private Dictionary<int, int> BuildScopesAndCollectConsts(string _file_path, string _text, string[] _lines, ScopeFrame _root_scope, List<ScopeFrame> _all_scopes)
		{
			Dictionary<int, int> _line_to_scope = new Dictionary<int, int>();
			Stack<int> _scope_stack = new Stack<int>();
			_scope_stack.Push(0);

			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;
			int _length = _text.Length;
			int _line_index = 0;
			int _line_start = 0;

			for (int _index = 0; _index < _length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _length ? _text[_index + 1] : '\0';

				if (_char == '\n')
				{
					_line_to_scope[_line_index] = _scope_stack.Peek();
					_line_index++;
					_line_start = _index + 1;
					if (_mode == GmlLexMode.LineComment)
					{
						_mode = GmlLexMode.Code;
					}
					continue;
				}

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						continue;
					case GmlLexMode.BlockComment:
						if (_char == '*' && _next == '/')
						{
							_mode = GmlLexMode.Code;
							_index++;
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
						}
						continue;
					case GmlLexMode.StringRawDouble:
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
						}
						continue;
					case GmlLexMode.StringRawSingle:
						if (_char == '\'')
						{
							_mode = GmlLexMode.Code;
						}
						continue;
					case GmlLexMode.TemplateText:
						if (_char == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
						}
						else if (_char == '"')
						{
							_mode = GmlLexMode.Code;
						}
						continue;
					case GmlLexMode.TemplateExpr:
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

						// Detect anchored const directives per line
						if (_index == _line_start)
						{
							this.ParseAnchoredConstDirective(_file_path, _lines, _line_index, _scope_stack.Peek(), _all_scopes);
						}

						// Detect function scopes
						if (GmlLexer.IsIdentifierStart(_char))
						{
							int _scan = _index;
							if (!GmlLexer.TryReadIdentifier(_text, ref _scan, out string _ident))
							{
								break;
							}

							if (_ident == "function")
							{
								int _after = GmlLexer.SkipWhitespace(_text, _scan);
								// Optional name
								int _temp = _after;
								if (GmlLexer.TryReadIdentifier(_text, ref _temp, out string _fname))
								{
									_after = GmlLexer.SkipWhitespace(_text, _temp);
								}

								if (_after < _text.Length && _text[_after] == '(')
								{
									// Parse params
									int _paren = _after;
									if (TrySkipBalanced(_text, ref _paren, '(', ')'))
									{
										int _brace = GmlLexer.SkipWhitespace(_text, _paren);
										if (_brace < _text.Length && _text[_brace] == '{')
										{
											// Enter new scope at this brace
											ScopeFrame _new_scope = new ScopeFrame();
											_all_scopes.Add(_new_scope);
											_scope_stack.Push(_all_scopes.Count - 1);
											_index = _brace; // Continue scanning from brace
											break;
										}
									}
								}
							}

							_index = _scan - 1;
						}

						// Exit scope on matching '}'
						if (_char == '}' && _scope_stack.Count > 1)
						{
							_scope_stack.Pop();
						}

						break;
				}
			}
			_line_to_scope[_line_index] = _scope_stack.Peek();
			return _line_to_scope;
		}

		private static bool TrySkipBalanced(string _text, ref int _index, char _open, char _close)
		{
			if (_index >= _text.Length || _text[_index] != _open)
			{
				return false;
			}
			int _depth = 0;
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;

			for (; _index < _text.Length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _text.Length ? _text[_index + 1] : '\0';

				if (_mode == GmlLexMode.Code)
				{
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
					if (_char == '"')
					{
						_mode = GmlLexMode.StringEsc;
						_escape = false;
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

					if (_char == _open)
					{
						_depth++;
					}
					else if (_char == _close)
					{
						_depth--;
						if (_depth == 0)
						{
							_index++;
							return true;
						}
					}
				}
				else if (_mode == GmlLexMode.LineComment)
				{
					if (_char == '\n')
					{
						_mode = GmlLexMode.Code;
					}
				}
				else if (_mode == GmlLexMode.BlockComment)
				{
					if (_char == '*' && _next == '/')
					{
						_mode = GmlLexMode.Code;
						_index++;
					}
				}
				else if (_mode == GmlLexMode.StringEsc)
				{
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
				}
				else if (_mode == GmlLexMode.StringRawDouble)
				{
					if (_char == '"')
					{
						_mode = GmlLexMode.Code;
					}
				}
				else if (_mode == GmlLexMode.StringRawSingle)
				{
					if (_char == '\'')
					{
						_mode = GmlLexMode.Code;
					}
				}
				else if (_mode == GmlLexMode.TemplateText)
				{
					if (_char == '{')
					{
						_mode = GmlLexMode.TemplateExpr;
					}
					else if (_char == '"')
					{
						_mode = GmlLexMode.Code;
					}
				}
				else if (_mode == GmlLexMode.TemplateExpr)
				{
					// treat as code
					_mode = GmlLexMode.Code;
					_index--;
				}
			}

			return false;
		}

		private void ParseAnchoredConstDirective(string _file_path, string[] _lines, int _line_index, int _scope_index, List<ScopeFrame> _all_scopes)
		{
			string _line = _lines[_line_index];
			int _idx = 0;
			while (_idx < _line.Length && (_line[_idx] == ' ' || _line[_idx] == '\t'))
			{
				_idx++;
			}
			if (!_line.AsSpan(_idx).StartsWith("const", StringComparison.Ordinal))
			{
				return;
			}

			// Quick anchored parse: const NAME = RHS
			int _scan = _idx + 5;
			while (_scan < _line.Length && (_line[_scan] == ' ' || _line[_scan] == '\t'))
			{
				_scan++;
			}
			int _name_start = _scan;
			while (_scan < _line.Length && GmlLexer.IsIdentifierPart(_line[_scan]))
			{
				_scan++;
			}
			if (_scan == _name_start)
			{
				return;
			}
			string _name = _line.Substring(_name_start, _scan - _name_start);

			while (_scan < _line.Length && (_line[_scan] == ' ' || _line[_scan] == '\t'))
			{
				_scan++;
			}
			if (_scan >= _line.Length || _line[_scan] != '=')
			{
				return;
			}
			_scan++;
			string _rhs = _scan < _line.Length ? _line.Substring(_scan).Trim() : string.Empty;

			ScopeFrame _scope = _all_scopes[_scope_index];
			if (_macro_table.TryGet(_name, out _))
			{
				throw new InvalidOperationException($"Const name conflicts with macro '{_name}' at {_file_path}:{_line_index + 1}");
			}
			if (_scope.ConstsByName.ContainsKey(_name))
			{
				throw new InvalidOperationException($"Const redefinition '{_name}' at {_file_path}:{_line_index + 1}");
			}

			_scope.ConstLineIndices.Add(_line_index);
			_scope.ConstsByName[_name] = new ConstInfo(_name, _line_index, ConstPlanKind.Static, _rhs);
		}

		private void ResolveConsts(string _file_path, string[] _lines, List<ScopeFrame> _all_scopes)
		{
			foreach (ScopeFrame _scope in _all_scopes)
			{
				// Resolve in order of line index
				List<ConstInfo> _consts = _scope.ConstsByName.Values.OrderBy(v => v.LineIndex).ToList();
				Dictionary<string, ConstInfo> _resolved = new Dictionary<string, ConstInfo>(StringComparer.Ordinal);

				foreach (ConstInfo _info in _consts)
				{
					string _rhs = _info.ValueText;

					// Expand macros in RHS
					_rhs = MacroExpander.ExpandAll(_rhs, _macro_table, new Stack<string>(), 64);

					// Substitute previously resolved inline consts in RHS
					_rhs = ConstSubstituter.SubstituteInlineAndAlias(_rhs, _resolved);

					// Alias to known const
					if (_resolved.TryGetValue(_rhs, out ConstInfo? _target))
					{
						if (_target.PlanKind == ConstPlanKind.Static)
						{
							_info.PlanKind = ConstPlanKind.Alias;
							_info.ValueText = _target.Name;
							_resolved[_info.Name] = _info;
							continue;
						}
						if (_target.PlanKind == ConstPlanKind.Inline)
						{
							_info.PlanKind = ConstPlanKind.Inline;
							_info.ValueText = _target.ValueText;
							_resolved[_info.Name] = _info;
							continue;
						}
					}

					if (ContainsArrayOrStructLiteral(_rhs))
					{
						_info.PlanKind = ConstPlanKind.Static;
						_info.ValueText = _rhs;
						_resolved[_info.Name] = _info;
						continue;
					}

					if (TinyEvaluator.TryEvaluate(_rhs, out string _literal))
					{
						_info.PlanKind = ConstPlanKind.Inline;
						_info.ValueText = _literal;
						_resolved[_info.Name] = _info;
						continue;
					}

					// Unknown or dynamic -> static
					_info.PlanKind = ConstPlanKind.Static;
					_info.ValueText = _rhs;
					_resolved[_info.Name] = _info;
				}

				// Write back resolved values
				foreach (var _pair in _resolved)
				{
					_scope.ConstsByName[_pair.Key] = _pair.Value;
				}
			}
		}

		private static bool ContainsArrayOrStructLiteral(string _rhs)
		{
			// Conservative scan in code mode ignoring strings/comments.
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;
			for (int _i = 0; _i < _rhs.Length; _i++)
			{
				char _c = _rhs[_i];
				char _n = _i + 1 < _rhs.Length ? _rhs[_i + 1] : '\0';

				switch (_mode)
				{
					case GmlLexMode.Code:
						if (_c == '/' && _n == '/')
						{
							_i = _rhs.Length;
							break;
						}
						if (_c == '/' && _n == '*')
						{
							_mode = GmlLexMode.BlockComment;
							_i++;
							break;
						}
						if (_c == '@' && _n == '"')
						{
							_mode = GmlLexMode.StringRawDouble;
							_i++;
							break;
						}
						if (_c == '@' && _n == '\'')
						{
							_mode = GmlLexMode.StringRawSingle;
							_i++;
							break;
						}
						if (_c == '$' && _n == '"')
						{
							_mode = GmlLexMode.TemplateText;
							_i++;
							break;
						}
						if (_c == '"')
						{
							_mode = GmlLexMode.StringEsc;
							_escape = false;
							break;
						}

						if (_c == '[' || _c == '{')
						{
							return true;
						}

						break;
					case GmlLexMode.BlockComment:
						if (_c == '*' && _n == '/')
						{
							_mode = GmlLexMode.Code;
							_i++;
						}
						break;
					case GmlLexMode.StringEsc:
						if (_escape)
						{
							_escape = false;
						}
						else if (_c == '\\')
						{
							_escape = true;
						}
						else if (_c == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;
					case GmlLexMode.StringRawDouble:
						if (_c == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;
					case GmlLexMode.StringRawSingle:
						if (_c == '\'')
						{
							_mode = GmlLexMode.Code;
						}
						break;
					case GmlLexMode.TemplateText:
						if (_c == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
						}
						else if (_c == '"')
						{
							_mode = GmlLexMode.Code;
						}
						break;
					case GmlLexMode.TemplateExpr:
						_mode = GmlLexMode.Code;
						_i--;
						break;
				}
			}

			return false;
		}

		private string ExpandLineWithScope(string _line, ScopeFrame _scope, ScopeFrame _root_scope)
		{
			// Expand macros and inline/alias consts.
			string _expanded = MacroExpander.ExpandAll(_line, _macro_table, new Stack<string>(), 256);
			_expanded = ConstSubstituter.SubstituteInlineAndAlias(_expanded, _scope.ConstsByName);
			_expanded = ConstSubstituter.SubstituteInlineAndAlias(_expanded, _root_scope.ConstsByName);
			return _expanded;
		}
	}

	internal static class ConstSubstituter
	{
		public static string SubstituteInlineAndAlias(string _text, IReadOnlyDictionary<string, ConstInfo> _consts)
		{
			if (_consts.Count == 0)
			{
				return _text;
			}

			StringBuilder _out = new StringBuilder(_text.Length);
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;
			char _prev_non_ws = '\0';

			for (int _index = 0; _index < _text.Length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _text.Length ? _text[_index + 1] : '\0';

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						_out.Append(_char);
						if (_char == '\n')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.BlockComment:
						_out.Append(_char);
						if (_char == '*' && _next == '/')
						{
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.StringEsc:
						_out.Append(_char);
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
						_out.Append(_char);
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.StringRawSingle:
						_out.Append(_char);
						if (_char == '\'')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.TemplateText:
						_out.Append(_char);
						if (_char == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
							_prev_non_ws = '{';
						}
						else if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.TemplateExpr:
						goto case GmlLexMode.Code;
					case GmlLexMode.Code:
						if (_char == '/' && _next == '/')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.LineComment;
							continue;
						}
						if (_char == '/' && _next == '*')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.BlockComment;
							continue;
						}
						if (_char == '@' && _next == '"')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.StringRawDouble;
							continue;
						}
						if (_char == '@' && _next == '\'')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.StringRawSingle;
							continue;
						}
						if (_char == '$' && _next == '"')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.TemplateText;
							continue;
						}
						if (_char == '"')
						{
							_out.Append(_char);
							_mode = GmlLexMode.StringEsc;
							_escape = false;
							continue;
						}

						if (GmlLexer.IsIdentifierStart(_char))
						{
							int _start = _index;
							int _scan = _index + 1;
							while (_scan < _text.Length && GmlLexer.IsIdentifierPart(_text[_scan]))
							{
								_scan++;
							}
							string _ident = _text.Substring(_start, _scan - _start);
							_index = _scan - 1;

							if (_prev_non_ws != '.' && _consts.TryGetValue(_ident, out ConstInfo? _info))
							{
								if (_info.PlanKind == ConstPlanKind.Inline)
								{
									_out.Append(_info.ValueText);
									_prev_non_ws = 'a';
									continue;
								}
								if (_info.PlanKind == ConstPlanKind.Alias)
								{
									_out.Append(_info.ValueText);
									_prev_non_ws = 'a';
									continue;
								}
							}

							_out.Append(_ident);
							_prev_non_ws = 'a';
							continue;
						}

						_out.Append(_char);
						if (_char != ' ' && _char != '\t' && _char != '\r' && _char != '\n')
						{
							_prev_non_ws = _char;
						}
						continue;
				}
			}

			return _out.ToString();
		}
	}

	internal static class MacroExpander
	{
		public static string ExpandAll(string _text, MacroTable _macro_table, Stack<string> _expansion_stack, int _max_steps)
		{
			string _current = _text;
			for (int _pass = 0; _pass < 32; _pass++)
			{
				string _next = ExpandOnce(_current, _macro_table, _expansion_stack, _max_steps);
				if (_next == _current)
				{
					return _current;
				}
				_current = _next;
			}
			return _current;
		}

		private static string ExpandOnce(string _text, MacroTable _macro_table, Stack<string> _expansion_stack, int _max_steps)
		{
			StringBuilder _out = new StringBuilder(_text.Length);
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;
			char _prev_non_ws = '\0';

			for (int _index = 0; _index < _text.Length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _text.Length ? _text[_index + 1] : '\0';

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						_out.Append(_char);
						if (_char == '\n')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.BlockComment:
						_out.Append(_char);
						if (_char == '*' && _next == '/')
						{
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.StringEsc:
						_out.Append(_char);
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
						_out.Append(_char);
						if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.StringRawSingle:
						_out.Append(_char);
						if (_char == '\'')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.TemplateText:
						_out.Append(_char);
						if (_char == '{')
						{
							_mode = GmlLexMode.TemplateExpr;
							_prev_non_ws = '{';
						}
						else if (_char == '"')
						{
							_mode = GmlLexMode.Code;
							_prev_non_ws = '\0';
						}
						continue;
					case GmlLexMode.TemplateExpr:
						goto case GmlLexMode.Code;
					case GmlLexMode.Code:
						if (_char == '/' && _next == '/')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.LineComment;
							continue;
						}
						if (_char == '/' && _next == '*')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.BlockComment;
							continue;
						}
						if (_char == '@' && _next == '"')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.StringRawDouble;
							continue;
						}
						if (_char == '@' && _next == '\'')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.StringRawSingle;
							continue;
						}
						if (_char == '$' && _next == '"')
						{
							_out.Append(_char);
							_out.Append(_next);
							_index++;
							_mode = GmlLexMode.TemplateText;
							continue;
						}
						if (_char == '"')
						{
							_out.Append(_char);
							_mode = GmlLexMode.StringEsc;
							_escape = false;
							continue;
						}

						if (GmlLexer.IsIdentifierStart(_char))
						{
							int _start = _index;
							int _scan = _index + 1;
							while (_scan < _text.Length && GmlLexer.IsIdentifierPart(_text[_scan]))
							{
								_scan++;
							}
							string _ident = _text.Substring(_start, _scan - _start);

							if (_prev_non_ws != '.' && _macro_table.TryGet(_ident, out MacroDefinition _macro))
							{
								// Lookahead for invocation
								int _after_ident = _scan;
								while (_after_ident < _text.Length && (_text[_after_ident] == ' ' || _text[_after_ident] == '\t'))
								{
									_after_ident++;
								}

								if (_macro.HasParameters)
								{
									if (_after_ident < _text.Length && _text[_after_ident] == '(')
									{
										if (_expansion_stack.Contains(_macro.Name))
										{
											throw new InvalidOperationException($"Macro recursion detected: {string.Join(" -> ", _expansion_stack.Reverse())} -> {_macro.Name}");
										}
										_expansion_stack.Push(_macro.Name);
										string _expanded = ExpandInvocation(_text, ref _after_ident, _macro, _macro_table, _expansion_stack);
										_expansion_stack.Pop();
										_out.Append(_expanded);
										_index = _after_ident - 1;
										_prev_non_ws = 'a';
										continue;
									}
								}
								else
								{
									// No params macro: replace identifier
									if (_expansion_stack.Contains(_macro.Name))
									{
										throw new InvalidOperationException($"Macro recursion detected: {string.Join(" -> ", _expansion_stack.Reverse())} -> {_macro.Name}");
									}
									_expansion_stack.Push(_macro.Name);
									string _expanded = ExpandAll(_macro.BodySingleLine, _macro_table, _expansion_stack, _max_steps - 1);
									_expansion_stack.Pop();
									_out.Append(_expanded);
									_index = _scan - 1;
									_prev_non_ws = 'a';
									continue;
								}
							}

							_out.Append(_ident);
							_index = _scan - 1;
							_prev_non_ws = 'a';
							continue;
						}

						_out.Append(_char);
						if (_char != ' ' && _char != '\t' && _char != '\r' && _char != '\n')
						{
							_prev_non_ws = _char;
						}
						continue;
				}
			}

			return _out.ToString();
		}

		private static string ExpandInvocation(string _text, ref int _index, MacroDefinition _macro, MacroTable _macro_table, Stack<string> _stack)
		{
			// _index currently points at '(' (after whitespace)
			List<string> _args = ParseArgumentList(_text, ref _index);
			if (_args.Count != _macro.Parameters.Length)
			{
				throw new InvalidOperationException($"Macro '{_macro.Name}' expected {_macro.Parameters.Length} args, got {_args.Count}");
			}

			Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
			for (int _i = 0; _i < _args.Count; _i++)
			{
				_map[_macro.Parameters[_i]] = _args[_i];
			}

			string _body = SubstituteParams(_macro.BodySingleLine, _map);
			return ExpandAll(_body, _macro_table, _stack, 128);
		}

		private static List<string> ParseArgumentList(string _text, ref int _index)
		{
			List<string> _args = new List<string>();
			if (_text[_index] != '(')
			{
				return _args;
			}
			_index++; // skip '('
			int _start = _index;
			int _paren_depth = 1;
			int _brace_depth = 0;
			int _bracket_depth = 0;
			GmlLexMode _mode = GmlLexMode.Code;
			bool _escape = false;

			for (; _index < _text.Length; _index++)
			{
				char _char = _text[_index];
				char _next = _index + 1 < _text.Length ? _text[_index + 1] : '\0';

				if (_mode == GmlLexMode.Code)
				{
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
					if (_char == '"')
					{
						_mode = GmlLexMode.StringEsc;
						_escape = false;
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

					if (_char == '(') _paren_depth++;
					else if (_char == ')')
					{
						_paren_depth--;
						if (_paren_depth == 0)
						{
							string _slice = _text.Substring(_start, _index - _start).Trim();
							if (_slice.Length > 0)
							{
								_args.Add(_slice);
							}
							_index++;
							return _args;
						}
					}
					else if (_char == '{') _brace_depth++;
					else if (_char == '}') _brace_depth = Math.Max(0, _brace_depth - 1);
					else if (_char == '[') _bracket_depth++;
					else if (_char == ']') _bracket_depth = Math.Max(0, _bracket_depth - 1);

					if (_char == ',' && _paren_depth == 1 && _brace_depth == 0 && _bracket_depth == 0)
					{
						string _slice = _text.Substring(_start, _index - _start).Trim();
						_args.Add(_slice);
						_start = _index + 1;
					}
				}
				else if (_mode == GmlLexMode.LineComment)
				{
					if (_char == '\n')
					{
						_mode = GmlLexMode.Code;
					}
				}
				else if (_mode == GmlLexMode.BlockComment)
				{
					if (_char == '*' && _next == '/')
					{
						_mode = GmlLexMode.Code;
						_index++;
					}
				}
				else if (_mode == GmlLexMode.StringEsc)
				{
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
				}
				else if (_mode == GmlLexMode.StringRawDouble)
				{
					if (_char == '"') _mode = GmlLexMode.Code;
				}
				else if (_mode == GmlLexMode.StringRawSingle)
				{
					if (_char == '\'') _mode = GmlLexMode.Code;
				}
				else if (_mode == GmlLexMode.TemplateText)
				{
					if (_char == '{') _mode = GmlLexMode.TemplateExpr;
					else if (_char == '"') _mode = GmlLexMode.Code;
				}
				else if (_mode == GmlLexMode.TemplateExpr)
				{
					_mode = GmlLexMode.Code;
					_index--;
				}
			}

			throw new InvalidOperationException("Unclosed macro invocation argument list");
		}

		private static string SubstituteParams(string _body, Dictionary<string, string> _param_map)
		{
			StringBuilder _out = new StringBuilder(_body.Length);
			int _index = 0;
			while (_index < _body.Length)
			{
				char _char = _body[_index];
				if (GmlLexer.IsIdentifierStart(_char))
				{
					int _start = _index;
					_index++;
					while (_index < _body.Length && GmlLexer.IsIdentifierPart(_body[_index]))
					{
						_index++;
					}
					string _ident = _body.Substring(_start, _index - _start);
					if (_param_map.TryGetValue(_ident, out string? _replacement))
					{
						_out.Append(_replacement);
					}
					else
					{
						_out.Append(_ident);
					}
					continue;
				}
				_out.Append(_char);
				_index++;
			}
			return _out.ToString();
		}
	}
}
