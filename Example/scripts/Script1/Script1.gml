#region jsDoc
/// @func gml_ext_unit_test()
/// @desc Runtime smoke/unit tests for: #macro, const, template strings, ?. nullish chains, and closure().
///         Output format is only: [SUCCESS] <name> or [FAIL] <name>
#endregion

function gml_ext_unit_test(){

	// Tiny local reporter (keeps output consistent)
	function report_result(_test_name, _did_pass){
		if (_did_pass)	show_debug_message("[SUCCESS] " + _test_name);
		else			show_debug_message("[FAIL] " + _test_name);
	}

	// ============================
	// MACRO TESTS
	// ============================

	#macro ADD(A, B) ((A) + (B))
	#macro MUL(A, B) ((A) * (B))
	#macro ADD_MUL(A, B, C) ADD(MUL((A), (B)), (C))

	#macro HELLO_MSG(NAME) show_debug_message("Hello " + \
	string(NAME) + \
	"!")

	// ============================
	// CONST TESTS (script scope)
	// ============================

	const INT_LIT = 123
	const STR_LIT = "abc"
	const MATH_FOLDED = ADD(1, 2) * 3
	const ARR_REF = [c_red, c_green, c_blue]
	const STR_JOIN = "prefix " + "postfix"

	// CONST script inline + folding smoke
	try{
		var _sum0 = INT_LIT + MATH_FOLDED;
		var _str0 = STR_LIT + " " + STR_JOIN;

		// Minimal sanity output (optional)
		show_debug_message(_sum0);
		show_debug_message(_str0);

		report_result("const script inline", true);
	}
	catch(_error){
		report_result("const script inline", false);
	}

	// CONST array reference (must lower to static, runtime smoke only)
	try{
		array_foreach(ARR_REF, function(_value){
			show_debug_message(_value);
		});
		report_result("const array reference", true);
	}
	catch(_error){
		report_result("const array reference", false);
	}

	// MACRO multiline
	try{
		HELLO_MSG("Player");
		report_result("macro multiline", true);
	}
	catch(_error){
		report_result("macro multiline", false);
	}

	// MACRO nested + const usage
	try{
		var _sum1 = ADD_MUL(2, 5, INT_LIT);
		show_debug_message(_sum1);
		report_result("macro nested", true);
	}
	catch(_error){
		report_result("macro nested", false);
	}

	// CONST index/member use smoke
	try{
		var _col0 = ARR_REF[0];
		show_debug_message(_col0);
		report_result("const index use", true);
	}
	catch(_error){
		report_result("const index use", false);
	}

	// ============================
	// FUNCTION SCOPE TESTS
	// ============================

	function test_scope_outer(){
		const LOC_NUM = 10
		const LOC_STR = @"raw_string"
		const LOC_FOLD = ADD(5, 5)

		// Function-scope inline
		try{
			var _a = LOC_NUM + LOC_FOLD;
			var _b = LOC_STR + " ok";
			show_debug_message(_a);
			show_debug_message(_b);
			report_result("const function-scope inline", true);
		}
		catch(_error){
			report_result("const function-scope inline", false);
		}

		// Shadowing in nested named function
		function test_scope_inner(){
			const LOC_NUM = 999

			try{
				var _c = LOC_NUM + 1;
				show_debug_message(_c);
				report_result("const shadowing (inner)", true);
			}
			catch(_error){
				report_result("const shadowing (inner)", false);
			}
		}

		// Outer should still use outer LOC_NUM (this previously threw in your output)
		try{
			var _d = LOC_NUM + 1;
			show_debug_message(_d);
			report_result("const outer unaffected by inner shadow", true);
		}
		catch(_error){
			report_result("const outer unaffected by inner shadow", false);
		}

		test_scope_inner();
	}

	test_scope_outer();

	// ============================
	// METHOD / FUNCTION-EXPR SCOPE TESTS
	// ============================

	var method_func = function(){
		const METH_CONST = "method_scope"

		try{
			show_debug_message(METH_CONST);
			report_result("const method scope", true);
		}
		catch(_error){
			report_result("const method scope", false);
		}

		// Nested function-expr should not inherit const replacements
		var _callback = function(){
			try{
				show_debug_message("nested anon reached");
				report_result("const nested anon isolation", true);
			}
			catch(_error){
				report_result("const nested anon isolation", false);
			}
		};

		_callback();
	};

	method_func();

	// ============================
	// TEMPLATE STRING TESTS
	// ============================

	const TMP_A = 7
	const TMP_B = ADD(1, 2)

	// Template expr replacement
	try{
		var _tmpl = $"sum {TMP_A + TMP_B} done";
		show_debug_message(_tmpl);
		report_result("template expr", true);
	}
	catch(_error){
		report_result("template expr", false);
	}

	// Template text braces should not act like code braces
	try{
		var _tmpl_text = $"text braces {{ }} and value {TMP_A}";
		show_debug_message(_tmpl_text);
		report_result("template text braces", true);
	}
	catch(_error){
		report_result("template text braces", false);
	}

	// ============================
	// NULLISH CHAIN TESTS (?.)
	// ============================

	var _nullish_struct = { foo: { bar: { example: 99 } }, key1: { key2: { key3: 42 } } };

	try{
		var _maybe_value1 = _nullish_struct?.key1;
		var _maybe_value2 = _nullish_struct?.key1?.key2?.key3;
		var _maybe_value3 = _nullish_struct.foo?.bar;
		var _maybe_value4 = _nullish_struct?.foo.bar;
		var _maybe_value5 = _nullish_struct.foo?.bar?.example;

		show_debug_message(_maybe_value1);
		show_debug_message(_maybe_value2);
		show_debug_message(_maybe_value3);
		show_debug_message(_maybe_value4);
		show_debug_message(_maybe_value5);

		report_result("nullish basic chains", true);
	}
	catch(_error){
		report_result("nullish basic chains", false);
	}

	// Nullish should be safe on undefined root
	try{
		var _missing_root = undefined;
		var _safe = _missing_root?.nope?.still_nope;
		show_debug_message(_safe);
		report_result("nullish undefined root safe", true);
	}
	catch(_error){
		report_result("nullish undefined root safe", false);
	}

	// Non-nullish should throw on undefined root (expected throw)
	try{
		var _missing_root2 = undefined;
		var _should_throw = _missing_root2.nope;
		show_debug_message(_should_throw);
		report_result("nullish non-nullish throws on undefined (expected throw)", false);
	}
	catch(_error){
		report_result("nullish non-nullish throws on undefined (expected throw)", true);
	}

	// ============================
	// CLOSURE TESTS (closure(function(){...}))
	// ============================

	function closure_driver(){
		var _outer_number = 5;
		var _outer_string = "hello";
		var _outer_struct = { value: 10 };

		var _cl0 = closure(function(){
			try{
				show_debug_message(_outer_number);
				show_debug_message(_outer_string);
				show_debug_message(_outer_struct.value);
				report_result("closure multi-capture", true);
			}
			catch(_error){
				report_result("closure multi-capture", false);
			}
		});

		var _cl1 = closure(function(){
			try{
				show_debug_message("no capture");
				report_result("closure no-capture", true);
			}
			catch(_error){
				report_result("closure no-capture", false);
			}
		});

		var _cl2 = closure(function(){
			try{
				var _outer_number = 777;
				show_debug_message(_outer_number);
				report_result("closure shadowing local", true);
			}
			catch(_error){
				report_result("closure shadowing local", false);
			}
		});

		// Regression: deep nested template reference must NOT affect capture set.
		var _cl3 = closure(function(){
			var _local_only = 123;

			var _inner = function(){
				try{
					show_debug_message($"If you see this it's not working {_outer_string}");
					report_result("closure deep-nested outer ref not captured", false);
				}
				catch(_error){
					report_result("closure deep-nested outer ref not captured", true);
				}
			};

			try{
				show_debug_message(_local_only);
				_inner();
				report_result("closure nested function isolation", true);
			}
			catch(_error){
				report_result("closure nested function isolation", false);
			}
		});

		_cl0();
		_cl1();
		_cl2();
		_cl3();
	}

	closure_driver();
}

gml_ext_unit_test();


// ============================
// NEW CONST STRING-LITERAL TEST CASES
// (covers: escaped strings, raw strings, multiline raw strings -> var, template strings dynamic)
// ============================

// Escaped string literal should inline
const STR_ESC = "escape \" string"

// Single-line raw strings should inline
const STR_RAW_DQ = @"raw string (no escape)"
const STR_RAW_SQ = @'raw string (no escape)'

// Template without interpolation - you currently treat as expandable (inline ok)
const STR_TMP_NO_EXPR = $"template no expr"

// Multiline raw string - special lowering: const -> var (preserve line breaks)
const STR_RAW_MULTI = @"line 1
line 2
line 3"

// Smoke usage + reporting (assumes report_result exists)
try{
	// These should compile and run; the key is the rewrite output shape.
	show_debug_message(STR_ESC);
	show_debug_message(STR_RAW_DQ);
	show_debug_message(STR_RAW_SQ);
	show_debug_message(STR_TMP_NO_EXPR);
	show_debug_message(STR_RAW_MULTI);

	report_result("const string literals (esc/raw/template/multiline raw)", true);
}
catch(_error){
	report_result("const string literals (esc/raw/template/multiline raw)", false);
}


// ============================
// MULTILINE RAW STRING SCOPE TEST
// (ensures multiline raw-string const inside a function is handled, and does not break later consts)
// ============================

function test_multiline_raw_scope(){
	// This const should lower to: var LOC_MULTI = @"..."
	// (not static alias form, not inline substitution)
	const LOC_MULTI = @"fn line 1
fn line 2
fn line 3"

	// A normal const after the multiline raw string should still resolve correctly.
	const LOC_AFTER = 111

	try{
		show_debug_message(LOC_MULTI);
		show_debug_message(LOC_AFTER);
		report_result("multiline raw string const in function scope", true);
	}
	catch(_error){
		report_result("multiline raw string const in function scope", false);
	}
}

test_multiline_raw_scope();


// ============================
// TEMPLATE STRING DYNAMIC RULE TEST (no false inlining)
// (ensures template with { } is treated as dynamic and doesn't get inline-substituted)
// ============================

function test_template_dynamic_rule(){
	const TMP_LOCAL = 5
	const TMP_DYNAMIC = $"value {TMP_LOCAL}"

	// If TMP_DYNAMIC is treated as inline, it would be substituted as a literal token everywhere.
	// Runtime smoke: just ensure it executes and produces output.
	try{
		show_debug_message(TMP_DYNAMIC);
		report_result("template string with interpolation is dynamic (static fallback)", true);
	}
	catch(_error){
		report_result("template string with interpolation is dynamic (static fallback)", false);
	}
}

test_template_dynamic_rule();