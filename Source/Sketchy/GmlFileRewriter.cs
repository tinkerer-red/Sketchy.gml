using System.Text;

namespace Sketchy
{
	enum ConstPlanKind
	{
		Inline,
		Static,
		Alias,
		VarMultilineRawString,
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

	// -------------------------------------------------------------------------
	// GmlSpan — describes one contiguous region of a GML source string.
	// -------------------------------------------------------------------------

	internal enum GmlSpanKind
	{
		/// <summary>Live GML code — identifiers, operators, brackets, etc.</summary>
		Code,
		/// <summary>// … \n   (the newline belongs to the *next* Code span)</summary>
		LineComment,
		/// <summary>/* … */</summary>
		BlockComment,
		/// <summary>"…"  (escape-aware)</summary>
		StringEsc,
		/// <summary>@"…"  or  @'…'  (raw strings)</summary>
		StringRaw,
		/// <summary>$"…{ — the literal text portion of a template string</summary>
		TemplateText,
		/// <summary>$"…{ … } — the expression portion of a template string</summary>
		TemplateExpr,
	}

	internal readonly struct GmlSpan
	{
		public readonly GmlSpanKind Kind;
		/// <summary>Inclusive start index in the original string.</summary>
		public readonly int Start;
		/// <summary>Exclusive end index in the original string.</summary>
		public readonly int End;

		public GmlSpan(GmlSpanKind kind, int start, int end)
		{
			Kind = kind;
			Start = start;
			End = end;
		}

		public bool IsCode => Kind == GmlSpanKind.Code || Kind == GmlSpanKind.TemplateExpr;
	}

	// -------------------------------------------------------------------------
	// GmlSpanWalker — splits a GML source string into GmlSpans.
	// This is the single authoritative implementation of the GML lex-mode FSM.
	// Every other component that needs to skip strings/comments uses this.
	// -------------------------------------------------------------------------

	internal static class GmlSpanWalker
	{
		/// <summary>
		/// Build an array of every <see cref="GmlSpan"/> in <paramref name="text"/>.
		/// Spans are ordered and together cover every character exactly once.
		/// This is the primary entry-point: callers build the array once and then
		/// use <see cref="IsCodeIndex"/> for O(log n) per-character code checks.
		/// </summary>
		public static GmlSpan[] BuildSpanArray(string text)
		{
			return BuildSpanArray(text, 0, text.Length);
		}

		/// <summary>
		/// Build a span array for the sub-range <c>[startIndex, endIndex)</c>.
		/// </summary>
		public static GmlSpan[] BuildSpanArray(string text, int startIndex, int endIndex)
		{
			var list = new List<GmlSpan>();
			Walk(text, startIndex, endIndex, list.Add);
			return list.ToArray();
		}

		/// <summary>
		/// Returns <c>true</c> if <paramref name="index"/> falls inside a code span
		/// (i.e. is not inside a string literal or comment).
		/// Uses binary search — O(log n) in the number of spans.
		/// </summary>
		public static bool IsCodeIndex(GmlSpan[] spans, int index)
		{
			int lo = 0, hi = spans.Length - 1;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				if (spans[mid].End <= index)       lo = mid + 1;
				else if (spans[mid].Start > index) hi = mid - 1;
				else return spans[mid].IsCode;
			}
			return false;
		}

		/// <summary>
		/// Walk <paramref name="text"/> and invoke <paramref name="onSpan"/> for every
		/// contiguous span.  Spans are emitted in order and together cover every
		/// character exactly once.  Prefer <see cref="BuildSpanArray"/> when the spans
		/// will be queried more than once.
		/// </summary>
		public static void Walk(string text, Action<GmlSpan> onSpan)
		{
			Walk(text, 0, text.Length, onSpan);
		}

		/// <summary>
		/// Walk a sub-range <c>[startIndex, endIndex)</c> of <paramref name="text"/>.
		/// </summary>
		public static void Walk(string text, int startIndex, int endIndex, Action<GmlSpan> onSpan)
		{
			GmlLexMode mode = GmlLexMode.Code;
			bool escape = false;
			int spanStart = startIndex;

			void Emit(GmlSpanKind kind, int excEnd)
			{
				if (excEnd > spanStart)
					onSpan(new GmlSpan(kind, spanStart, excEnd));
				spanStart = excEnd;
			}

			for (int i = startIndex; i < endIndex; i++)
			{
				char c = text[i];
				char n = i + 1 < endIndex ? text[i + 1] : '\0';

				switch (mode)
				{
					case GmlLexMode.LineComment:
						if (c == '\n')
						{
							Emit(GmlSpanKind.LineComment, i);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.BlockComment:
						if (c == '*' && n == '/')
						{
							i++;
							Emit(GmlSpanKind.BlockComment, i + 1);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.StringEsc:
						if (escape)       { escape = false; }
						else if (c == '\\') { escape = true; }
						else if (c == '"')
						{
							Emit(GmlSpanKind.StringEsc, i + 1);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.StringRawDouble:
						if (c == '"')
						{
							Emit(GmlSpanKind.StringRaw, i + 1);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.StringRawSingle:
						if (c == '\'')
						{
							Emit(GmlSpanKind.StringRaw, i + 1);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.TemplateText:
						if (c == '{')
						{
							Emit(GmlSpanKind.TemplateText, i + 1);
							mode = GmlLexMode.TemplateExpr;
						}
						else if (c == '"')
						{
							Emit(GmlSpanKind.TemplateText, i + 1);
							mode = GmlLexMode.Code;
						}
						break;

					case GmlLexMode.TemplateExpr:
						goto case GmlLexMode.Code;

					case GmlLexMode.Code:
						if (c == '/' && n == '/')
						{
							Emit(GmlSpanKind.Code, i);
							i++;
							mode = GmlLexMode.LineComment;
							spanStart = i - 1;
							break;
						}
						if (c == '/' && n == '*')
						{
							Emit(GmlSpanKind.Code, i);
							i++;
							mode = GmlLexMode.BlockComment;
							spanStart = i - 1;
							break;
						}
						if (c == '@' && n == '"')
						{
							Emit(GmlSpanKind.Code, i);
							i++;
							mode = GmlLexMode.StringRawDouble;
							spanStart = i - 1;
							break;
						}
						if (c == '@' && n == '\'')
						{
							Emit(GmlSpanKind.Code, i);
							i++;
							mode = GmlLexMode.StringRawSingle;
							spanStart = i - 1;
							break;
						}
						if (c == '$' && n == '"')
						{
							Emit(GmlSpanKind.Code, i);
							i++;
							mode = GmlLexMode.TemplateText;
							spanStart = i - 1;
							break;
						}
						if (c == '"')
						{
							Emit(GmlSpanKind.Code, i);
							mode = GmlLexMode.StringEsc;
							escape = false;
							spanStart = i;
							break;
						}
						if (mode == GmlLexMode.TemplateExpr && c == '}')
						{
							Emit(GmlSpanKind.TemplateExpr, i);
							mode = GmlLexMode.TemplateText;
							spanStart = i;
						}
						break;
				}
			}

			if (spanStart < endIndex)
			{
				GmlSpanKind trailing = mode switch
				{
					GmlLexMode.LineComment    => GmlSpanKind.LineComment,
					GmlLexMode.BlockComment   => GmlSpanKind.BlockComment,
					GmlLexMode.StringEsc      => GmlSpanKind.StringEsc,
					GmlLexMode.StringRawDouble => GmlSpanKind.StringRaw,
					GmlLexMode.StringRawSingle => GmlSpanKind.StringRaw,
					GmlLexMode.TemplateText   => GmlSpanKind.TemplateText,
					GmlLexMode.TemplateExpr   => GmlSpanKind.TemplateExpr,
					_                         => GmlSpanKind.Code,
				};
				onSpan(new GmlSpan(trailing, spanStart, endIndex));
			}
		}

		// ------------------------------------------------------------------
		// Convenience helpers built on top of BuildSpanArray
		// ------------------------------------------------------------------

		/// <summary>
		/// Rewrite <paramref name="text"/> by applying <paramref name="transformCode"/>
		/// to every Code/TemplateExpr span while copying all other spans verbatim.
		/// The callback receives the full text and the span so it can read surrounding
		/// context; return <c>null</c> to leave the span unchanged.
		/// </summary>
		public static string RewriteCodeSpans(string text, GmlSpan[] spans, Func<string, GmlSpan, string?> transformCode)
		{
			bool anyChange = false;
			var sb = new StringBuilder(text.Length + 32);

			foreach (GmlSpan span in spans)
			{
				if (span.IsCode)
				{
					string? replacement = transformCode(text, span);
					string original = text.Substring(span.Start, span.End - span.Start);
					if (replacement != null && replacement != original)
					{
						sb.Append(replacement);
						anyChange = true;
					}
					else
					{
						sb.Append(original);
					}
				}
				else
				{
					sb.Append(text, span.Start, span.End - span.Start);
				}
			}

			return anyChange ? sb.ToString() : text;
		}

		/// <summary>Overload that builds the span array internally (convenience).</summary>
		public static string RewriteCodeSpans(string text, Func<string, GmlSpan, string?> transformCode)
		{
			return RewriteCodeSpans(text, BuildSpanArray(text), transformCode);
		}
		
	}
	// -------------------------------------------------------------------------
	// GmlFileRewriter
	// -------------------------------------------------------------------------

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

			// Second pass: per-line scope-aware rewrites (macro expand, const substitute,
			// directive suppression).  Nullish and closure transforms are deferred to a
			// third pass on the full assembled text because both constructs can span
			// multiple lines.
			string[] _out_lines = new string[_lines.Length];
			for (int _line_index = 0; _line_index < _lines.Length; _line_index++)
			{
				string _line = _lines[_line_index];

				if (_macro_def_lines.Contains(_line_index))
				{
					_out_lines[_line_index] = "//" + _line;
					continue;
				}

				int _scope_index = _line_to_scope_index.TryGetValue(_line_index, out int _value_scope) ? _value_scope : 0;
				ScopeFrame _scope = _all_scopes[_scope_index];

				if (_scope.ConstLineIndices.Contains(_line_index))
				{
					ConstInfo? _info = _scope.ConstsByName.Values.FirstOrDefault(v => v.LineIndex == _line_index);
					if (_info == null)
					{
						_out_lines[_line_index] = "//" + _line;
						continue;
					}

					if (_info.PlanKind == ConstPlanKind.VarMultilineRawString)
					{
						_out_lines[_line_index] = ReplaceAnchoredConstWithVar(_line);
						continue;
					}

					if (_info.PlanKind == ConstPlanKind.Static)
					{
						bool _is_root_scope = (_scope_index == 0);
						_out_lines[_line_index] = BuildStaticConstLine(_line, _info, _is_root_scope);
						continue;
					}

					_out_lines[_line_index] = "//" + _line;
					continue;
				}

				// Non-directive line: expand macros + const substitution (scope-aware).
				_out_lines[_line_index] = ApplyMacroConstPipeline(_line, _scope, _all_scopes[0]);
			}

			// Third pass: reassemble and apply full-text transforms.
			// Nullish (?.) and closure(function(){}) both can span multiple lines, so
			// they must operate on the complete output text, not individual lines.
			string _assembled = string.Join("\n", _out_lines);
			string _after_nullish  = ApplyNullishOperatorTransform(_assembled);
			string _after_closure  = ApplyClosureTransform(_after_nullish);

			bool _did_modify = !ReferenceEquals(_after_closure, _normalized) && _after_closure != _normalized;
			return new ProcessResult(_did_modify, _after_closure);
		}

		// -------------------------------------------------------------------------
		// Nullish Operator Transform
		// -------------------------------------------------------------------------
		// Rewrites ?. chains to bracket/struct-get forms.
		// Now uses GmlSpanWalker to avoid touching strings and comments.
		// -------------------------------------------------------------------------

		private static string ApplyNullishOperatorTransform(string line)
		{
			GmlSpan[] spans = GmlSpanWalker.BuildSpanArray(line);

			// Quick rejection: ?. must appear in a code span.
			bool hasNullish = false;
			foreach (GmlSpan span in spans)
			{
				if (!span.IsCode) continue;
				for (int k = span.Start; k < span.End - 1; k++)
				{
					if (line[k] == '?' && line[k + 1] == '.') { hasNullish = true; break; }
				}
				if (hasNullish) break;
			}
			if (!hasNullish) return line;

			return GmlSpanWalker.RewriteCodeSpans(line, spans, (text, span) =>
			{
				int spanLen = span.End - span.Start;
				// Quick check: does this span contain ?.
				bool found = false;
				for (int k = span.Start; k < span.End - 1; k++)
					if (text[k] == '?' && text[k + 1] == '.') { found = true; break; }
				if (!found) return null;

				var sb = new StringBuilder();
				int i = span.Start;
				while (i < span.End)
				{
					int qidx = text.IndexOf("?.", i, StringComparison.Ordinal);
					if (qidx < 0 || qidx >= span.End)
					{
						sb.Append(text, i, span.End - i);
						break;
					}

					// Find the start of the base expression.
					int start = qidx - 1;
					while (start >= span.Start && (char.IsLetterOrDigit(text[start]) || text[start] == '_' || text[start] == '.'))
						start--;
					start++;

					sb.Append(text, i, start - i);
					string baseExpr = text.Substring(start, qidx - start);

					// Parse the full chain of ?. and . accesses.
					int j = qidx;
					var segments = new List<(bool nullish, string name)>();
					while (j < span.End)
					{
						if (j + 1 < span.End && text[j] == '?' && text[j + 1] == '.')
						{
							int ns = j + 2, ne = ns;
							while (ne < span.End && (char.IsLetterOrDigit(text[ne]) || text[ne] == '_')) ne++;
							segments.Add((true, text.Substring(ns, ne - ns)));
							j = ne;
						}
						else if (text[j] == '.' && j + 1 < span.End && text[j + 1] != '.')
						{
							int ns = j + 1, ne = ns;
							while (ne < span.End && (char.IsLetterOrDigit(text[ne]) || text[ne] == '_')) ne++;
							segments.Add((false, text.Substring(ns, ne - ns)));
							j = ne;
						}
						else break;
					}

					int firstDot = segments.FindIndex(s => !s.nullish);
					if (firstDot == 0)
					{
						sb.Append(baseExpr); sb.Append('.'); sb.Append(segments[0].name);
						for (int k = 1; k < segments.Count; k++)
						{
							if (segments[k].nullish) { sb.Append("[$ \""); sb.Append(segments[k].name); sb.Append("\"]"); }
							else { sb.Append('.'); sb.Append(segments[k].name); }
						}
					}
					else if (firstDot > 0)
					{
						sb.Append(baseExpr); sb.Append("[$ \""); sb.Append(segments[0].name); sb.Append("\"]");
						for (int k = 1; k < segments.Count; k++)
						{
							if (segments[k].nullish) { sb.Append("[$ \""); sb.Append(segments[k].name); sb.Append("\"]"); }
							else { sb.Append('.'); sb.Append(segments[k].name); }
						}
					}
					else if (segments.All(s => s.nullish))
					{
						sb.Append("__struct_get_hashes("); sb.Append(baseExpr);
						foreach (var seg in segments) { sb.Append(", variable_get_hash(\""); sb.Append(seg.name); sb.Append("\")"); }
						sb.Append(')');
					}
					else
					{
						sb.Append(text, start, j - start);
					}
					i = j;
				}

				string result = sb.ToString();
				return result == text.Substring(span.Start, span.End - span.Start) ? null : result;
			});
		}
		// -------------------------------------------------------------------------
		// Closure Transform
		// -------------------------------------------------------------------------
		// Rewrites closure(function(){...}) to a method() + captured-variable form.
		// Now uses GmlSpanWalker to avoid touching strings and comments.
		// -------------------------------------------------------------------------

		// -----------------------------------------------------------------------
		// -----------------------------------------------------------------------
		// Collect all var-declared names from a region of text (span-aware).
		// Does NOT skip nested function bodies: all var declarations visible
		// in the text before the closure() call are capture candidates.
		// The intersection with bodyUsed handles filtering.
		// -----------------------------------------------------------------------
		private static HashSet<string> CollectVarDeclarations(string text, int regionStart, int regionEnd, GmlSpan[] spans)
		{
			var declared = new HashSet<string>(StringComparer.Ordinal);
			int i = regionStart;

			while (i < regionEnd)
			{
				if (!GmlSpanWalker.IsCodeIndex(spans, i)) { i++; continue; }

				// Look for 'var' keyword.
				if (i + 3 <= regionEnd &&
				    string.Compare(text, i, "var", 0, 3, StringComparison.Ordinal) == 0 &&
				    (i == 0 || !GmlLexer.IsIdentifierPart(text[i - 1])) &&
				    (i + 3 >= text.Length || !GmlLexer.IsIdentifierPart(text[i + 3])))
				{
					int scan = i + 3;
					while (scan < regionEnd)
					{
						// Skip non-code and whitespace (newlines are whitespace in GML).
						while (scan < regionEnd && (!GmlSpanWalker.IsCodeIndex(spans, scan) || char.IsWhiteSpace(text[scan]))) scan++;

						if (scan >= regionEnd || !GmlLexer.IsIdentifierStart(text[scan])) break;

						int nameStart = scan;
						while (scan < regionEnd && GmlLexer.IsIdentifierPart(text[scan])) scan++;
						declared.Add(text.Substring(nameStart, scan - nameStart));

						// Skip optional initialiser (balanced brackets, span-aware).
						while (scan < regionEnd && GmlSpanWalker.IsCodeIndex(spans, scan) && char.IsWhiteSpace(text[scan])) scan++;
						if (scan < regionEnd && GmlSpanWalker.IsCodeIndex(spans, scan) && text[scan] == '=')
						{
							scan++;
							int dP = 0, dB = 0, dBr = 0;
							while (scan < regionEnd)
							{
								if (!GmlSpanWalker.IsCodeIndex(spans, scan)) { scan++; continue; }
								char cc = text[scan];
								if      (cc == '(') dP++;
								else if (cc == ')') { if (--dP < 0) break; }
								else if (cc == '{') dB++;
								else if (cc == '}') { if (--dB < 0) break; }
								else if (cc == '[') dBr++;
								else if (cc == ']') { if (--dBr < 0) break; }
								else if ((cc == ',' || cc == ';') && dP == 0 && dB == 0 && dBr == 0) break;
								scan++;
							}
						}

						// Skip non-code/whitespace before the separator.
						while (scan < regionEnd && (!GmlSpanWalker.IsCodeIndex(spans, scan) || char.IsWhiteSpace(text[scan]))) scan++;
						if (scan >= regionEnd || !GmlSpanWalker.IsCodeIndex(spans, scan)) break;

						if (text[scan] == ',')
						{
							int peek = scan + 1;
							while (peek < regionEnd && (!GmlSpanWalker.IsCodeIndex(spans, peek) || char.IsWhiteSpace(text[peek]))) peek++;
							if (peek + 3 <= regionEnd &&
							    string.Compare(text, peek, "var", 0, 3, StringComparison.Ordinal) == 0 &&
							    (peek + 3 >= text.Length || !GmlLexer.IsIdentifierPart(text[peek + 3])))
								scan = peek + 3; // form 3: skip repeated 'var'
							else
								scan++;           // form 4: next declarator after comma
							continue;
						}
						break; // ';' or anything else ends the var statement
					}
					i = scan;
					continue;
				}

				if (GmlLexer.IsIdentifierStart(text[i]))
				{
					while (i < regionEnd && GmlLexer.IsIdentifierPart(text[i])) i++;
					continue;
				}
				i++;
			}
			return declared;
		}

		// -----------------------------------------------------------------------
		// Collect identifiers used at the IMMEDIATE level of a closure body.
		// Nested function(){} blocks are skipped so their identifiers do not
		// appear as capture candidates from the outer scope.
		// Strings and comments are skipped via IsCodeIndex.
		// -----------------------------------------------------------------------
		private static HashSet<string> CollectUsedIdentifiers(string text, int regionStart, int regionEnd, GmlSpan[] spans)
		{
			var used = new HashSet<string>(StringComparer.Ordinal);
			int i = regionStart;

			while (i < regionEnd)
			{
				if (!GmlSpanWalker.IsCodeIndex(spans, i)) { i++; continue; }

				// Skip nested function bodies entirely.
				if (i + 8 <= regionEnd &&
				    string.Compare(text, i, "function", 0, 8, StringComparison.Ordinal) == 0 &&
				    (i == 0 || !GmlLexer.IsIdentifierPart(text[i - 1])) &&
				    (i + 8 >= text.Length || !GmlLexer.IsIdentifierPart(text[i + 8])))
				{
					int scan = i + 8;
					while (scan < regionEnd && (!GmlSpanWalker.IsCodeIndex(spans, scan) || char.IsWhiteSpace(text[scan]))) scan++;
					if (scan < regionEnd && GmlSpanWalker.IsCodeIndex(spans, scan) && text[scan] == '(')
					{
						int depth = 1; scan++;
						while (scan < regionEnd && depth > 0)
						{
							if (GmlSpanWalker.IsCodeIndex(spans, scan))
							{ if (text[scan] == '(') depth++; else if (text[scan] == ')') depth--; }
							scan++;
						}
					}
					while (scan < regionEnd && (!GmlSpanWalker.IsCodeIndex(spans, scan) || char.IsWhiteSpace(text[scan]))) scan++;
					if (scan < regionEnd && GmlSpanWalker.IsCodeIndex(spans, scan) && text[scan] == '{')
					{
						int depth = 1; scan++;
						while (scan < regionEnd && depth > 0)
						{
							if (GmlSpanWalker.IsCodeIndex(spans, scan))
							{ if (text[scan] == '{') depth++; else if (text[scan] == '}') depth--; }
							scan++;
						}
					}
					i = scan;
					continue;
				}

				if (GmlLexer.IsIdentifierStart(text[i]))
				{
					int start = i;
					while (i < regionEnd && GmlLexer.IsIdentifierPart(text[i])) i++;
					used.Add(text.Substring(start, i - start));
					continue;
				}
				i++;
			}
			return used;
		}

				private static string ApplyClosureTransform(string text)
		{
			GmlSpan[] spans = GmlSpanWalker.BuildSpanArray(text);

			// Quick rejection: trigger must appear in a code span.
			const string trigger = "closure(function(";
			bool found = false;
			foreach (GmlSpan span in spans)
			{
				if (!span.IsCode) continue;
				for (int k = span.Start; k <= span.End - trigger.Length; k++)
					if (string.Compare(text, k, trigger, 0, trigger.Length, StringComparison.Ordinal) == 0) { found = true; break; }
				if (found) break;
			}
			if (!found) return text;

			var result = new StringBuilder(text.Length + 256);
			int i = 0;

			while (i < text.Length)
			{
				// Find the next closure( in a code span.
				int triggerIdx = -1;
				for (int k = i; k <= text.Length - trigger.Length; k++)
				{
					if (!GmlSpanWalker.IsCodeIndex(spans, k)) continue;
					if (string.Compare(text, k, trigger, 0, trigger.Length, StringComparison.Ordinal) == 0)
					{
						triggerIdx = k;
						break;
					}
				}

				if (triggerIdx < 0)
				{
					result.Append(text, i, text.Length - i);
					break;
				}

				result.Append(text, i, triggerIdx - i);

				// Find the opening brace of the function body.
				int sigStart = triggerIdx + "closure(".Length;
				int braceOpen = text.IndexOf('{', sigStart);
				if (braceOpen < 0) { result.Append(text, triggerIdx, text.Length - triggerIdx); break; }

				// Find matching closing brace (span-aware).
				int depth = 1;
				int pos = braceOpen + 1;
				while (pos < text.Length && depth > 0)
				{
					if (GmlSpanWalker.IsCodeIndex(spans, pos))
					{
						if (text[pos] == '{') depth++;
						else if (text[pos] == '}') depth--;
					}
					pos++;
				}
				if (depth != 0) { result.Append(text, triggerIdx, text.Length - triggerIdx); break; }

				int braceClose = pos - 1;

				// Find closing ')' of closure(...) on the same closer line.
				int closeParenIdx = braceClose + 1;
				while (closeParenIdx < text.Length && (text[closeParenIdx] == ' ' || text[closeParenIdx] == '\t'))
					closeParenIdx++;
				if (closeParenIdx >= text.Length || text[closeParenIdx] != ')')
				{
					result.Append(text, triggerIdx, pos - triggerIdx);
					i = pos;
					continue;
				}

				// ------------------------------------------------------------------
				// Determine captured variables.
				//
				// The rule: capture = (vars declared in the OUTER scope before this
				// closure) ∩ (identifiers referenced in the immediate closure body,
				// excluding nested function bodies).
				//
				// "Outer scope" = the text from the start of the file up to triggerIdx.
				// We collect var declarations from that region, then intersect with
				// identifiers actually used in the closure body (immediate level only).
				// ------------------------------------------------------------------
				int bodyStart = braceOpen + 1;
				int bodyEnd   = braceClose;

				// Collect vars declared in the outer scope (before the closure call).
				HashSet<string> outerVars = CollectVarDeclarations(text, 0, triggerIdx, spans);

				// Collect vars declared inside the immediate closure body (to exclude them).
				HashSet<string> bodyDeclared = CollectVarDeclarations(text, bodyStart, bodyEnd, spans);

				// Collect identifiers actually referenced at the immediate closure level.
				HashSet<string> bodyUsed = CollectUsedIdentifiers(text, bodyStart, bodyEnd, spans);

				// Captures = used in body (immediate level) AND declared in outer scope
				// AND not re-declared inside this closure body.
				var captures = new SortedSet<string>(StringComparer.Ordinal);
				foreach (string v in bodyUsed)
				{
					if (outerVars.Contains(v) && !bodyDeclared.Contains(v))
						captures.Add(v);
				}
				// ------------------------------------------------------------------
				// Line-count-preserving rewrite.
				//
				// If no outer vars are captured (captures is empty), the closure
				// needs no method() wrapper at all — emit function(){...} directly.
				//
				// Otherwise wrap with method({__closure_this: self, cap: cap, ...})
				// and inject "var cap = __closure_cap;" unpacking on the opener line.
				// __closure_this is special: it is used only for "with(__closure_this){";
				// there is no "var __closure_this = ..." unpacking needed.
				// ------------------------------------------------------------------
				string funcBody = text.Substring(bodyStart, bodyEnd - bodyStart);

				if (captures.Count == 0)
				{
					// No captures — emit bare function(){body} with no method() wrapper.
					// The original closure(...) parens are consumed entirely; no ')' needed.
					result.Append("function(){");
					result.Append(funcBody);
					result.Append("}");
				}
				else
				{
					var opener = new StringBuilder();
					opener.Append("method({__closure_this: self");
					foreach (string v in captures)
					{
						opener.Append(", ");
						opener.Append($"__closure_{v}"); opener.Append(": "); opener.Append(v);
					}
					opener.Append("}, function(){");
					// Unpack each captured var — __closure_this is NOT unpacked here;
					// it is used directly in "with(__closure_this){" below.
					foreach (string v in captures)
						opener.Append($"var {v} = __closure_{v}; ");
					opener.Append("with(__closure_this){");

					result.Append(opener);
					result.Append(funcBody);
					result.Append("}}");
					result.Append(')');
				}

				i = closeParenIdx + 1;
			}

			string output = result.ToString();
			return output == text ? text : output;
		}
		// -------------------------------------------------------------------------
		// Remaining private helpers (unchanged in interface, but now delegate
		// string/comment detection to GmlSpanWalker where applicable)
		// -------------------------------------------------------------------------

		private HashSet<int> CollectMacroDefinitionLines(string _file_path)
		{
			HashSet<int> _set = new HashSet<int>();
			foreach (MacroDefinition _macro in _macro_table.MacrosByName.Values)
			{
				if (!string.Equals(_macro.FilePath, _file_path, StringComparison.OrdinalIgnoreCase))
					continue;
				for (int _line = _macro.LineStart; _line <= _macro.LineEnd; _line++)
					_set.Add(_line);
			}
			return _set;
		}

		private static string BuildStaticConstLine(string _original_line, ConstInfo _info, bool _is_root_scope)
		{
			string _trimmed = _original_line.TrimEnd();
			bool _has_semicolon = (_trimmed.Length > 0 && _trimmed[_trimmed.Length - 1] == ';');

			string _rhs  = _info.ValueText.Trim();
			string _name = _info.Name;

			if (_is_root_scope)
			{
				string _line = $"var {_name} = (function(){{static __ = {_rhs}; return __;}})()";
				if (_has_semicolon) _line += ";";
				return _line;
			}

			string _static_name = $"__const_{_name}";
			string _out = $"static {_static_name} = {_rhs}; var {_name} = {_static_name}";
			if (_has_semicolon) _out += ";";
			return _out;
		}

		private static string ReplaceAnchoredConstWithVar(string _line)
		{
			int _idx = 0;
			while (_idx < _line.Length && (_line[_idx] == ' ' || _line[_idx] == '\t'))
				_idx++;

			// Assumes ParseAnchoredConstDirective already ensured this is a const directive.
			// Replace exactly "const" with "var".
			return _line.Substring(0, _idx) + "var" + _line.Substring(_idx + 5);
		}

		private Dictionary<int, int> BuildScopesAndCollectConsts(string _file_path, string _text, string[] _lines, ScopeFrame _root_scope, List<ScopeFrame> _all_scopes)
		{
			Dictionary<int, int> _line_to_scope = new Dictionary<int, int>();

			Stack<int> _scope_stack = new Stack<int>();
			_scope_stack.Push(0);

			// Tracks brace depth for each pushed function scope (does not include root).
			Stack<int> _brace_depth_stack = new Stack<int>();

			int _line_index = 0;
			int _line_start = 0;

			// We use GmlSpanWalker here via a manual character scan so we can still
			// track line numbers and react to individual characters inside Code spans.
			// The walker callback approach doesn't give us per-character line tracking,
			// so we replicate the FSM but delegate its transitions to a thin wrapper
			// that keeps line counters in sync.

			GmlSpanWalker.Walk(_text, span =>
			{
				// For every character in this span, advance line bookkeeping and,
				// for Code spans, apply scope/const detection logic.
				for (int _index = span.Start; _index < span.End; _index++)
				{
					char _char = _text[_index];

					if (_char == '\n')
					{
						_line_to_scope[_line_index] = _scope_stack.Peek();
						_line_index++;
						_line_start = _index + 1;
						continue;
					}

					if (!span.IsCode) continue;

					// Anchored const directives (must be at line start in a code span).
					if (_index == _line_start)
						ParseAnchoredConstDirective(_file_path, _lines, _line_index, _scope_stack.Peek(), _all_scopes);

					// Detect function keyword to push a new scope.
					if (GmlLexer.IsIdentifierStart(_char))
					{
						int _scan = _index;
						if (!GmlLexer.TryReadIdentifier(_text, ref _scan, out string _ident))
							continue;

						if (_ident == "function")
						{
							int _after = GmlLexer.SkipWhitespace(_text, _scan);
							int _temp = _after;
							if (GmlLexer.TryReadIdentifier(_text, ref _temp, out string _))
								_after = GmlLexer.SkipWhitespace(_text, _temp);

							if (_after < _text.Length && _text[_after] == '(')
							{
								int _paren = _after;
								if (TrySkipBalanced(_text, ref _paren, '(', ')'))
								{
									int _brace = GmlLexer.SkipWhitespace(_text, _paren);
									if (_brace < _text.Length && _text[_brace] == '{')
									{
										// This '{' is also inside the parent function body, so it must count toward
										// the parent's depth. We skip processing the char later, so we adjust now.
										if (_brace_depth_stack.Count > 0)
										{
											int _parent_depth = _brace_depth_stack.Pop();
											_parent_depth++;
											_brace_depth_stack.Push(_parent_depth);
										}

										ScopeFrame _new_scope = new ScopeFrame();
										_all_scopes.Add(_new_scope);

										_scope_stack.Push(_all_scopes.Count - 1);

										// New function body starts at depth 1 (its opening brace).
										_brace_depth_stack.Push(1);

										// Skip the '{' character so we don't increment the new scope to 2.
										_index = _brace;
										continue;
									}
								}
							}
						}

						_index = _scan - 1;
					}

					// Track braces only when inside a function scope (brace stack mirrors pushed scopes).
					if (_brace_depth_stack.Count > 0)
					{
						if (_char == '{')
						{
							int _depth = _brace_depth_stack.Pop();
							_depth++;
							_brace_depth_stack.Push(_depth);
						}
						else if (_char == '}')
						{
							int _depth = _brace_depth_stack.Pop();
							_depth--;
							if (_depth == 0)
							{
								// End of this function scope.
								_scope_stack.Pop();
							}
							else
							{
								_brace_depth_stack.Push(_depth);
							}
						}
					}
				}
			});

			_line_to_scope[_line_index] = _scope_stack.Peek();
			return _line_to_scope;
		}

		/// <summary>
		/// Skips a balanced pair of <paramref name="_open"/>/<paramref name="_close"/>
		/// delimiters using <see cref="GmlSpanWalker"/> to ignore string/comment content.
		/// </summary>
		private static bool TrySkipBalanced(string _text, ref int _index, char _open, char _close)
		{
			if (_index >= _text.Length || _text[_index] != _open)
				return false;

			// Capture into locals — ref params cannot be captured by lambdas.
			int _startIndex = _index;
			int _depth = 0;
			int _result = -1;

			// We only care about Code-span characters for bracket counting.
			GmlSpanWalker.Walk(_text, _startIndex, _text.Length, span =>
			{
				if (_result >= 0) return; // already found
				if (!span.IsCode) return;
				int start = Math.Max(span.Start, _startIndex);
				for (int i = start; i < span.End; i++)
				{
					if (_text[i] == _open)  _depth++;
					else if (_text[i] == _close)
					{
						_depth--;
						if (_depth == 0) { _result = i + 1; return; }
					}
				}
			});

			if (_result < 0) return false;
			_index = _result;
			return true;
		}

		private void ParseAnchoredConstDirective(string _file_path, string[] _lines, int _line_index, int _scope_index, List<ScopeFrame> _all_scopes)
		{
			string _line = _lines[_line_index];
			int _idx = 0;
			while (_idx < _line.Length && (_line[_idx] == ' ' || _line[_idx] == '\t'))
				_idx++;

			if (!_line.AsSpan(_idx).StartsWith("const", StringComparison.Ordinal))
				return;

			int _scan = _idx + 5;
			while (_scan < _line.Length && (_line[_scan] == ' ' || _line[_scan] == '\t'))
				_scan++;

			int _name_start = _scan;
			while (_scan < _line.Length && GmlLexer.IsIdentifierPart(_line[_scan]))
				_scan++;

			if (_scan == _name_start) return;
			string _name = _line.Substring(_name_start, _scan - _name_start);

			while (_scan < _line.Length && (_line[_scan] == ' ' || _line[_scan] == '\t'))
				_scan++;

			if (_scan >= _line.Length || _line[_scan] != '=') return;
			_scan++;

			while (_scan < _line.Length && (_line[_scan] == ' ' || _line[_scan] == '\t'))
				_scan++;

			string _rhs = string.Empty;

			// Multi-line raw string support: @"..." or @'...'
			if (_scan + 1 < _line.Length && _line[_scan] == '@' && (_line[_scan + 1] == '"' || _line[_scan + 1] == '\''))
			{
				char _quote = _line[_scan + 1];

				System.Text.StringBuilder _builder = new System.Text.StringBuilder();

				// First line: start at the '@'
				string _first_segment = _line.Substring(_scan);

				// Search for terminator quote on the first segment, after the opener (@")
				int _found = _first_segment.IndexOf(_quote, 2);
				if (_found >= 0)
				{
					// Single-line raw string, capture only the literal token up to closing quote.
					_rhs = _first_segment.Substring(0, _found + 1).Trim();
				}
				else
				{
					// Multi-line raw string: accumulate full lines until the first matching quote.
					_builder.Append(_first_segment);

					int _search_line_index = _line_index + 1;
					bool _closed = false;

					while (_search_line_index < _lines.Length)
					{
						string _next_line = _lines[_search_line_index];

						_builder.Append('\n');

						int _end_index = _next_line.IndexOf(_quote);
						if (_end_index >= 0)
						{
							_builder.Append(_next_line.Substring(0, _end_index + 1));
							_closed = true;
							break;
						}

						_builder.Append(_next_line);
						_search_line_index++;
					}

					if (!_closed)
						throw new InvalidOperationException($"Unterminated raw string const '{_name}' at {_file_path}:{_line_index + 1}");

					_rhs = _builder.ToString();
				}
			}
			else
			{
				// Single-line RHS fallback (existing behavior)
				_rhs = _scan < _line.Length ? _line.Substring(_scan).Trim() : string.Empty;
			}

			ScopeFrame _scope = _all_scopes[_scope_index];
			if (_macro_table.TryGet(_name, out _))
				throw new InvalidOperationException($"Const name conflicts with macro '{_name}' at {_file_path}:{_line_index + 1}");
			if (_scope.ConstsByName.ContainsKey(_name))
				throw new InvalidOperationException($"Const redefinition '{_name}' at {_file_path}:{_line_index + 1}");

			_scope.ConstLineIndices.Add(_line_index);
			_scope.ConstsByName[_name] = new ConstInfo(_name, _line_index, ConstPlanKind.Static, _rhs);
		}

		private void ResolveConsts(string _file_path, string[] _lines, List<ScopeFrame> _all_scopes)
		{
			foreach (ScopeFrame _scope in _all_scopes)
			{
				List<ConstInfo> _consts = _scope.ConstsByName.Values.OrderBy(v => v.LineIndex).ToList();
				Dictionary<string, ConstInfo> _resolved = new Dictionary<string, ConstInfo>(StringComparer.Ordinal);

				foreach (ConstInfo _info in _consts)
				{
					string _rhs = _info.ValueText;
					_rhs = MacroExpander.ExpandAll(_rhs, _macro_table, new Stack<string>(), 64);
					_rhs = ConstSubstituter.SubstituteInlineAndAlias(_rhs, _resolved);

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

					if (TryClassifyStringConstRhs(_rhs, out ConstPlanKind _string_plan, out string _string_value))
					{
						_info.PlanKind = _string_plan;
						_info.ValueText = _string_value;
						_resolved[_info.Name] = _info;
						continue;
					}

					// Treat certain string forms as inlineable literals even if TinyEvaluator
					// does not "evaluate" them.
					if (TryClassifyStringConstRhs(_rhs, out bool _inlineable, out string _inline_text))
					{
						_info.PlanKind = _inlineable ? ConstPlanKind.Inline : ConstPlanKind.Static;
						_info.ValueText = _inlineable ? _inline_text : _rhs;
						_resolved[_info.Name] = _info;
						continue;
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

					_info.PlanKind = ConstPlanKind.Static;
					_info.ValueText = _rhs;
					_resolved[_info.Name] = _info;
				}

				foreach (var _pair in _resolved)
					_scope.ConstsByName[_pair.Key] = _pair.Value;
			}
		}

		/// <summary>
		/// Returns true if the RHS contains a '[' or '{' outside of strings/comments,
		/// indicating an array or struct literal.
		/// </summary>
		private static bool ContainsArrayOrStructLiteral(string _rhs)
		{
			foreach (GmlSpan span in GmlSpanWalker.BuildSpanArray(_rhs))
			{
				if (!span.IsCode) continue;
				for (int i = span.Start; i < span.End; i++)
					if (_rhs[i] == '[' || _rhs[i] == '{') return true;
			}
			return false;
		}

		private static bool TryClassifyStringConstRhs(string _rhs, out ConstPlanKind _plan, out string _value)
		{
			_plan = ConstPlanKind.Static;
			_value = _rhs;

			string _trim = _rhs.Trim();
			if (_trim.Length == 0) return false;

			GmlSpan[] _spans = GmlSpanWalker.BuildSpanArray(_trim);

			// "escaped string"
			if (_spans.Length == 1 && _spans[0].Kind == GmlSpanKind.StringEsc && _spans[0].Start == 0 && _spans[0].End == _trim.Length)
			{
				_plan = ConstPlanKind.Inline;
				_value = _trim;
				return true;
			}

			// @"raw" or @'raw'
			if (_spans.Length == 1 && _spans[0].Kind == GmlSpanKind.StringRaw && _spans[0].Start == 0 && _spans[0].End == _trim.Length)
			{
				if (_trim.IndexOf('\n') >= 0)
				{
					// Special case: multiline raw string becomes `var NAME = <literal>` (line-preserving).
					_plan = ConstPlanKind.VarMultilineRawString;
					_value = _trim;
					return true;
				}

				_plan = ConstPlanKind.Inline;
				_value = _trim;
				return true;
			}

			// $"template"
			// Inline only if it contains no TemplateExpr spans (no { } interpolation).
			bool _has_template_text = false;
			bool _has_template_expr = false;

			for (int _span_index = 0; _span_index < _spans.Length; _span_index++)
			{
				GmlSpanKind _kind = _spans[_span_index].Kind;

				if (_kind == GmlSpanKind.TemplateText) _has_template_text = true;
				else if (_kind == GmlSpanKind.TemplateExpr) _has_template_expr = true;
				else if (_kind != GmlSpanKind.Code)
				{
					// comments/strings shouldn't appear here if the span array is correct, but treat as non-literal RHS
					return false;
				}
			}

			if (_has_template_text)
			{
				if (_has_template_expr)
				{
					_plan = ConstPlanKind.Static;
					_value = _trim;
					return true;
				}

				_plan = ConstPlanKind.Inline;
				_value = _trim;
				return true;
			}

			return false;
		}

		private static bool TryClassifyStringConstRhs(string _rhs, out bool _inlineable, out string _inline_text)
		{
			_inlineable = false;
			_inline_text = _rhs;

			string _trim = _rhs.Trim();
			if (_trim.Length == 0) return false;

			GmlSpan[] _spans = GmlSpanWalker.BuildSpanArray(_trim);

			// Case 1: normal escaped string literal "..."
			// Entire RHS must be exactly one StringEsc span.
			if (_spans.Length == 1 && _spans[0].Kind == GmlSpanKind.StringEsc && _spans[0].Start == 0 && _spans[0].End == _trim.Length)
			{
				_inlineable = true;
				_inline_text = _trim;
				return true;
			}

			// Case 2: raw string literal @"..." or @'...'
			// Inlineable only if the literal contains no newline.
			if (_spans.Length == 1 && _spans[0].Kind == GmlSpanKind.StringRaw && _spans[0].Start == 0 && _spans[0].End == _trim.Length)
			{
				bool _has_newline = _trim.IndexOf('\n') >= 0;
				_inlineable = !_has_newline;
				_inline_text = _trim;
				return true;
			}

			// Case 3: template string $"..."
			// Inlineable only if it has NO TemplateExpr spans (no { } interpolation).
			// A no-interpolation template string will be a single TemplateText span that covers the whole literal.
			// If TemplateExpr exists anywhere, treat as non-inlineable (Static).
			bool _has_template_text = false;
			bool _has_template_expr = false;
			for (int _span_index = 0; _span_index < _spans.Length; _span_index++)
			{
				GmlSpanKind _kind = _spans[_span_index].Kind;
				if (_kind == GmlSpanKind.TemplateText) _has_template_text = true;
				else if (_kind == GmlSpanKind.TemplateExpr) _has_template_expr = true;
				else if (_kind == GmlSpanKind.Code)
				{
					// Any code outside the template literal means it isn't a pure literal RHS.
					return false;
				}
			}

			if (_has_template_text)
			{
				_inlineable = !_has_template_expr;
				_inline_text = _trim;
				return true;
			}

			return false;
		}

		private string ApplyMacroConstPipeline(string _line, ScopeFrame _scope, ScopeFrame _root_scope)
		{
			// Step 1: substitute inline/alias consts before macro expansion
			string _step0 = ConstSubstituter.SubstituteInlineAndAlias(_line, _scope.ConstsByName);
			_step0 = ConstSubstituter.SubstituteInlineAndAlias(_step0, _root_scope.ConstsByName);

			// Step 2: expand macros
			string _step1 = MacroExpander.ExpandAll(_step0, _macro_table, new Stack<string>(), 256);

			// Step 3: substitute again to catch const identifiers introduced by macro bodies
			string _step2 = ConstSubstituter.SubstituteInlineAndAlias(_step1, _scope.ConstsByName);
			_step2 = ConstSubstituter.SubstituteInlineAndAlias(_step2, _root_scope.ConstsByName);

			return _step2;
		}
	}

	// -------------------------------------------------------------------------
	// ConstSubstituter
	// -------------------------------------------------------------------------
	// Rewrites identifiers that match known Inline/Alias consts while skipping
	// strings and comments.  Now delegates span detection to GmlSpanWalker.
	// -------------------------------------------------------------------------

	internal static class ConstSubstituter
	{
		public static string SubstituteInlineAndAlias(string _text, IReadOnlyDictionary<string, ConstInfo> _consts)
		{
			if (_consts.Count == 0) return _text;

			GmlSpan[] spans = GmlSpanWalker.BuildSpanArray(_text);
			return GmlSpanWalker.RewriteCodeSpans(_text, spans, (text, span) =>
			{
				StringBuilder? _out = null;
				char _prev_non_ws = '\0';
				int i = span.Start;

				while (i < span.End)
				{
					char c = text[i];

					if (GmlLexer.IsIdentifierStart(c))
					{
						int start = i++;
						while (i < span.End && GmlLexer.IsIdentifierPart(text[i])) i++;
						string ident = text.Substring(start, i - start);

						if (_prev_non_ws != '.' && _consts.TryGetValue(ident, out ConstInfo? info) &&
						    (info.PlanKind == ConstPlanKind.Inline || info.PlanKind == ConstPlanKind.Alias))
						{
							_out ??= new StringBuilder(text.Substring(span.Start, start - span.Start));
							_out.Append(info.ValueText);
							_prev_non_ws = 'a';
							continue;
						}

						_out?.Append(ident);
						_prev_non_ws = 'a';
						continue;
					}

					_out?.Append(c);
					if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
						_prev_non_ws = c;
					i++;
				}

				return _out?.ToString();
			});
		}
	}
	// -------------------------------------------------------------------------
	// MacroExpander
	// -------------------------------------------------------------------------
	// Expands macros in source text while skipping strings and comments.
	// Delegates span detection to GmlSpanWalker.
	// -------------------------------------------------------------------------

	internal static class MacroExpander
	{
		public static string ExpandAll(string _text, MacroTable _macro_table, Stack<string> _expansion_stack, int _max_steps)
		{
			string _current = _text;
			for (int _pass = 0; _pass < 32; _pass++)
			{
				string _next = ExpandOnce(_current, _macro_table, _expansion_stack, _max_steps);
				if (_next == _current) return _current;
				_current = _next;
			}
			return _current;
		}

		private static string ExpandOnce(string _text, MacroTable _macro_table, Stack<string> _expansion_stack, int _max_steps)
		{
			// Build span array once for this text. We iterate the full text with an
			// IsCodeIndex check so that ParseArgumentList receives absolute indices
			// into _text and can correctly cross string-literal spans inside arguments
			// (e.g. FOO("hello, world", x)).
			GmlSpan[] spans = GmlSpanWalker.BuildSpanArray(_text);

			StringBuilder? _out = null;
			char _prev_non_ws = '\0';
			int i = 0;

			while (i < _text.Length)
			{
				// Skip non-code characters, copying them verbatim.
				if (!GmlSpanWalker.IsCodeIndex(spans, i))
				{
					_out?.Append(_text[i]);
					_prev_non_ws = '\0';
					i++;
					continue;
				}

				char c = _text[i];

				if (GmlLexer.IsIdentifierStart(c))
				{
					int start = i;
					i++;
					while (i < _text.Length && GmlLexer.IsIdentifierPart(_text[i])) i++;
					string ident = _text.Substring(start, i - start);

					if (_prev_non_ws != '.' && _macro_table.TryGet(ident, out MacroDefinition _macro))
					{
						int afterIdent = i;
						while (afterIdent < _text.Length && (_text[afterIdent] == ' ' || _text[afterIdent] == '\t'))
							afterIdent++;

						if (_macro.HasParameters)
						{
							if (afterIdent < _text.Length && _text[afterIdent] == '(')
							{
								if (_expansion_stack.Contains(_macro.Name))
									throw new InvalidOperationException($"Macro recursion detected: {string.Join(" -> ", _expansion_stack.Reverse())} -> {_macro.Name}");

								_expansion_stack.Push(_macro.Name);
								string expanded = ExpandInvocation(_text, ref afterIdent, _macro, _macro_table, _expansion_stack);
								_expansion_stack.Pop();

								_out ??= new StringBuilder(_text.Substring(0, start));
								_out.Append(expanded);
								i = afterIdent;
								_prev_non_ws = 'a';
								continue;
							}
						}
						else
						{
							if (_expansion_stack.Contains(_macro.Name))
								throw new InvalidOperationException($"Macro recursion detected: {string.Join(" -> ", _expansion_stack.Reverse())} -> {_macro.Name}");

							_expansion_stack.Push(_macro.Name);
							string expanded = ExpandAll(_macro.BodySingleLine, _macro_table, _expansion_stack, _max_steps - 1);
							_expansion_stack.Pop();

							_out ??= new StringBuilder(_text.Substring(0, start));
							_out.Append(expanded);
							_prev_non_ws = 'a';
							continue;
						}
					}

					_out?.Append(ident);
					_prev_non_ws = 'a';
					continue;
				}

				_out?.Append(c);
				if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
					_prev_non_ws = c;
				i++;
			}

			return _out?.ToString() ?? _text;
		}

		private static string ExpandInvocation(string _text, ref int _index, MacroDefinition _macro, MacroTable _macro_table, Stack<string> _stack)
		{
			List<string> _args = ParseArgumentList(_text, ref _index);
			if (_args.Count != _macro.Parameters.Length)
				throw new InvalidOperationException($"Macro '{_macro.Name}' expected {_macro.Parameters.Length} args, got {_args.Count}");

			Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
			for (int _i = 0; _i < _args.Count; _i++)
				_map[_macro.Parameters[_i]] = _args[_i];

			string _body = SubstituteParams(_macro.BodySingleLine, _map);
			return ExpandAll(_body, _macro_table, _stack, 128);
		}

		private static List<string> ParseArgumentList(string _text, ref int _index)
		{
			// _text is a code slice produced by RewriteCodeSpans, so the surrounding
			// strings/comments have already been excluded.  However, macro arguments
			// can themselves contain string literals (e.g. FOO("hi", x)), so we run
			// a lightweight inline FSM here to avoid treating delimiters inside
			// those nested strings as argument separators.
			List<string> _args = new List<string>();
			if (_text[_index] != '(') return _args;

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

				switch (_mode)
				{
					case GmlLexMode.LineComment:
						if (_char == '\n') _mode = GmlLexMode.Code;
						continue;
					case GmlLexMode.BlockComment:
						if (_char == '*' && _next == '/') { _mode = GmlLexMode.Code; _index++; }
						continue;
					case GmlLexMode.StringEsc:
						if (_escape) { _escape = false; }
						else if (_char == '\\') { _escape = true; }
						else if (_char == '"') { _mode = GmlLexMode.Code; }
						continue;
					case GmlLexMode.StringRawDouble:
						if (_char == '"') _mode = GmlLexMode.Code;
						continue;
					case GmlLexMode.StringRawSingle:
						if (_char == '\'') _mode = GmlLexMode.Code;
						continue;
					case GmlLexMode.TemplateText:
						if (_char == '{') _mode = GmlLexMode.TemplateExpr;
						else if (_char == '"') _mode = GmlLexMode.Code;
						continue;
					case GmlLexMode.TemplateExpr:
						goto case GmlLexMode.Code;
					case GmlLexMode.Code:
						if (_char == '/' && _next == '/') { _mode = GmlLexMode.LineComment; _index++; continue; }
						if (_char == '/' && _next == '*') { _mode = GmlLexMode.BlockComment; _index++; continue; }
						if (_char == '"') { _mode = GmlLexMode.StringEsc; _escape = false; continue; }
						if (_char == '@' && _next == '"') { _mode = GmlLexMode.StringRawDouble; _index++; continue; }
						if (_char == '@' && _next == '\'') { _mode = GmlLexMode.StringRawSingle; _index++; continue; }
						if (_char == '$' && _next == '"') { _mode = GmlLexMode.TemplateText; _index++; continue; }

						if (_char == '(') _paren_depth++;
						else if (_char == ')')
						{
							_paren_depth--;
							if (_paren_depth == 0)
							{
								string _slice = _text.Substring(_start, _index - _start).Trim();
								if (_slice.Length > 0) _args.Add(_slice);
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
						break;
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
				char c = _body[_index];
				if (GmlLexer.IsIdentifierStart(c))
				{
					int start = _index++;
					while (_index < _body.Length && GmlLexer.IsIdentifierPart(_body[_index])) _index++;
					string ident = _body.Substring(start, _index - start);
					_out.Append(_param_map.TryGetValue(ident, out string? rep) ? rep : ident);
					continue;
				}
				_out.Append(c);
				_index++;
			}
			return _out.ToString();
		}
	}
}