using System.Diagnostics;
using System.Reflection;

namespace Sketchy
{
	internal static class Program
	{
		private static bool _verbose = false;
		private static string _archive_path = string.Empty;

		public static int Main(string[] _arguments)
		{
			try
			{
				if (_arguments.Length < 2 || _arguments.Length > 3)
				{
					Console.WriteLine("[Sketchy] Wrong number of arguments. Use: Sketchy <ProjectDir> --pre|--post|--clean [--verbose]");
					return 1;
				}

				string _project_path = _arguments[0];
				string _mode = _arguments[1];
				_verbose = (_arguments.Length == 3 && _arguments[2] == "--verbose");

				_archive_path = ComputeArchivePath();

				string[] _gml_files = EnumerateGmlFiles(_project_path);

				switch (_mode)
				{
					case "--pre":
						return RunPre(_project_path, _gml_files);
					case "--post":
						return RunPost(_gml_files);
					case "--clean":
						return RunClean(_project_path, _gml_files);
					default:
						Console.WriteLine("[Sketchy] Unknown mode: " + _mode);
						return 1;
				}
			}
			catch (Exception _exception)
			{
				Console.WriteLine("[Sketchy] Fatal Error: " + _exception);
				return 1;
			}
		}

		private static int RunPre(string _project_path, string[] _gml_files)
		{
			Console.WriteLine("[Sketchy] Version: " + Assembly.GetExecutingAssembly().GetName().Version);

			Restore(_gml_files);

			MacroTable _macro_table = BuildGlobalMacroTable(_gml_files);
			Console.WriteLine("[Sketchy] Loaded " + _macro_table.MacrosByName.Count + " macros");
			GmlProcessor _processor = new GmlProcessor(_macro_table);

			Stopwatch _stopwatch = Stopwatch.StartNew();
			int _modified_count = 0;

			foreach (string _file_path in _gml_files)
			{
				if (_verbose) Console.WriteLine($"[Sketchy Debug] Processing file: {_file_path}");
				string _text = File.ReadAllText(_file_path);

				if (!GmlProcessor.FileContainsDirectivesOrMacroRefs(_text, _macro_table.MacrosByName))
				{
					if (_verbose) Console.WriteLine($"[Sketchy Debug] Skipped (no directives/macros): {_file_path}");
					continue;
				}

				ProcessResult _result = _processor.PreprocessText(_file_path, _text);

				// Always refresh .gml_mod as the expanded view for participating files.
				string _mod_path = _file_path + "_mod";
				File.WriteAllText(_mod_path, _result.OutputText);

				// Only touch the real .gml when content changes.
				if (string.Equals(_result.OutputText, _text, StringComparison.Ordinal))
				{
					if (_verbose) Console.WriteLine($"[Sketchy Debug] No changes for: {_file_path}");
					continue;
				}

				string _bak_path = _file_path + "_bak";
				File.Copy(_file_path, _bak_path, true);
				File.WriteAllText(_file_path, _result.OutputText);

				if (_verbose) Console.WriteLine($"[Sketchy Debug] Modified: {_file_path}");
				_modified_count++;
			}

			_stopwatch.Stop();
			Console.WriteLine($"[Sketchy] Pre complete. Modified {_modified_count} files in {_stopwatch.ElapsedMilliseconds} ms");
			return 0;
		}

		private static int RunPost(string[] _gml_files)
		{
			Console.WriteLine("[Sketchy] Restore originals");
			Restore(_gml_files, _archive_path);
			return 0;
		}

		private static int RunClean(string _project_path, string[] _gml_files)
		{
			Console.WriteLine("[Sketchy] Restore originals");
			Restore(_gml_files, _archive_path);

			Console.WriteLine("[Sketchy] Clean cache");
			foreach (string _file in _gml_files)
			{
				string _mod_path = _file + "_mod";
				if (File.Exists(_mod_path))
				{
					File.Delete(_mod_path);
				}
			}

			return 0;
		}

		private static MacroTable BuildGlobalMacroTable(string[] _gml_files)
		{
			MacroTable _table = new MacroTable();

			foreach (string _file in _gml_files)
			{
				string _text = File.ReadAllText(_file);
				string _normalized = _text.Replace("\r\n", "\n").Replace("\r", "\n");
				string[] _lines = _normalized.Split('\n');

				bool _in_block_comment = false;
				for (int _line_index = 0; _line_index < _lines.Length; _line_index++)
				{
					string _line = _lines[_line_index];
					int _idx = 0;
					while (_idx < _line.Length && (_line[_idx] == ' ' || _line[_idx] == '\t'))
					{
						_idx++;
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

					if (!_line.AsSpan(_idx).StartsWith("#macro", StringComparison.Ordinal))
					{
						continue;
					}

					// Collect macro continuation block
					List<string> _body_lines = new List<string>();
					int _start_line = _line_index;

					string _first = _line.Substring(_idx);
					_body_lines.Add(_first);
					bool _cont = GmlLexer.EndsWithContinuationBackslash(_line, false);
					while (_cont)
					{
						_line_index++;
						if (_line_index >= _lines.Length)
						{
							break;
						}
						string _next_line = _lines[_line_index];
						_body_lines.Add(_next_line);
						_cont = GmlLexer.EndsWithContinuationBackslash(_next_line, false);
					}

					int _end_line = _line_index;

					MacroDefinition _macro = ParseMacroBlock(_file, _body_lines, _start_line, _end_line);
					_table.AddOrThrow(_macro);
				}
			}

			return _table;
		}

		private static MacroDefinition ParseMacroBlock(string _file_path, List<string> _lines, int _line_start, int _line_end)
		{
			// First line contains signature.
			string _first = _lines[0].Trim();
			// Strip '#macro'
			string _rest = _first.Substring(6).Trim();
			int _index = 0;
			if (!GmlLexer.TryReadIdentifier(_rest, ref _index, out string _name))
			{
				throw new InvalidOperationException($"Invalid macro at {_file_path}:{_line_start + 1}");
			}

			string[] _parameters = Array.Empty<string>();
			_index = GmlLexer.SkipWhitespace(_rest, _index);
			if (_index < _rest.Length && _rest[_index] == '(')
			{
				int _paren_index = _index;
				if (!TryParseMacroParams(_rest, ref _paren_index, out _parameters))
				{
					throw new InvalidOperationException($"Invalid macro params for '{_name}' at {_file_path}:{_line_start + 1}");
				}
				_index = _paren_index;
			}

			string _body_first_line = _rest.Substring(_index).Trim();
			List<string> _body_only_lines = new List<string>();
			_body_only_lines.Add(RemoveTrailingContinuation(_body_first_line));

			for (int _i = 1; _i < _lines.Count; _i++)
			{
				_body_only_lines.Add(RemoveTrailingContinuation(_lines[_i]));
			}

			string _body_single_line = GmlLexer.JoinLinesSingleLine(_body_only_lines);
			return new MacroDefinition(_name, _parameters, _body_single_line, _file_path, _line_start, _line_end);
		}

		private static string RemoveTrailingContinuation(string _line)
		{
			string _trim = _line.TrimEnd();
			if (_trim.EndsWith("\\", StringComparison.Ordinal))
			{
				return _trim.Substring(0, _trim.Length - 1);
			}
			return _line;
		}

		private static bool TryParseMacroParams(string _text, ref int _index, out string[] _parameters)
		{
			_parameters = Array.Empty<string>();
			if (_index >= _text.Length || _text[_index] != '(')
			{
				return false;
			}
			_index++;
			List<string> _parts = new List<string>();
			while (_index < _text.Length)
			{
				_index = GmlLexer.SkipWhitespace(_text, _index);
				if (_index < _text.Length && _text[_index] == ')')
				{
					_index++;
					_parameters = _parts.ToArray();
					return true;
				}
				if (!GmlLexer.TryReadIdentifier(_text, ref _index, out string _param))
				{
					return false;
				}
				_parts.Add(_param);
				_index = GmlLexer.SkipWhitespace(_text, _index);
				if (_index < _text.Length && _text[_index] == ',')
				{
					_index++;
					continue;
				}
			}
			return false;
		}

		private static string[] EnumerateGmlFiles(string _project_path)
		{
			return Directory.EnumerateFiles(_project_path, "*.gml", SearchOption.AllDirectories)
				.Where(_path => !IsInIgnoredDirectory(_project_path, _path))
				.ToArray();
		}

		private static bool IsInIgnoredDirectory(string _project_path, string _path)
		{
			string _relative = Path.GetRelativePath(_project_path, _path).Replace('\\', '/');
			if (_relative.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (_relative.StartsWith("options/", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (_relative.StartsWith("datafiles/", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return false;
		}

		private static string ComputeArchivePath()
		{
			string? _archive_name = Environment.GetEnvironmentVariable("YYMACROS_project_cache_directory_name");
			string? _archive_dir = Environment.GetEnvironmentVariable("YYMACROS_ide_cache_directory");

			if (!string.IsNullOrEmpty(_archive_name) && !string.IsNullOrEmpty(_archive_dir))
			{
				return Path.GetFullPath(_archive_name + "\\Sketchy", _archive_dir);
			}

			string? _temp = Environment.GetEnvironmentVariable("YYtempFolder");
			if (!string.IsNullOrEmpty(_temp))
			{
				return Path.GetFullPath(_temp + "\\Sketchy");
			}

			return string.Empty;
		}

		private static void Restore(string[] _files, string _archive_path = "")
		{
			foreach (string _file in _files)
			{
				string _bak = _file + "_bak";
				if (!File.Exists(_bak))
				{
					continue;
				}

				if (!string.IsNullOrEmpty(_archive_path))
				{
					ArchiveBackup(_file, _bak, _archive_path);
				}

				File.Copy(_bak, _file, true);
				File.Delete(_bak);
			}
		}

		private static void ArchiveBackup(string _file_path, string _bak_path, string _archive_path)
		{
			if (!Directory.Exists(_archive_path))
			{
				Directory.CreateDirectory(_archive_path);
			}

			string _filename = Path.GetFileName(_file_path);
			bool _need_archive = true;
			string? _last = Directory.EnumerateFiles(_archive_path, _filename + "_*")
				.OrderByDescending(_file => _file)
				.FirstOrDefault();

			if (_last != null)
			{
				FileInfo _last_info = new FileInfo(_last);
				FileInfo _bak_info = new FileInfo(_bak_path);
				if (_last_info.Length == _bak_info.Length && _last_info.LastWriteTime == _bak_info.LastWriteTime)
				{
					_need_archive = false;
				}
			}

			if (_need_archive)
			{
				string _timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string _archive_file = Path.GetFullPath(_archive_path + "\\" + _filename + "_" + _timestamp);
				File.Copy(_bak_path, _archive_file, false);

				foreach (string _old in Directory.EnumerateFiles(_archive_path, _filename + "_*").OrderByDescending(_f => _f).Skip(5))
				{
					File.Delete(_old);
				}
			}
		}
	}
}
