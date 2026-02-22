/// @func const

// ============================
// MACRO TESTS (parameterized, nested, multi-line, single-line output)
// ============================

#macro ADD(A, B) ((A) + (B))
#macro MUL(A, B) ((A) * (B))
#macro ADD_MUL(A, B, C) ADD(MUL((A), (B)), (C))

#macro HELLO_MSG(NAME) show_debug_message("Hello " + \
string(NAME) + \
"!")

// ============================
// CONST TESTS (scoped, inline literal, static rewrite, macro inside const + folding)
// ============================

const INT_LIT = 123
const STR_LIT = "abc"
const MATH_FOLDED = ADD(1, 2) * 3
const ARR_REF = [c_red, c_green, c_blue]
const STR_JOIN = "prefix " + "postfix"

// Use consts at script scope (should inline where possible, rewrite ARR_REF to static)
var _sum0 = INT_LIT + MATH_FOLDED;
var _str0 = STR_LIT + " " + STR_JOIN;
show_debug_message(_sum0);
show_debug_message(_str0);

// Array const used as a reference (must be static rewrite, not inline)
array_foreach(ARR_REF, function(_value){
	show_debug_message(_value);
});

// Multi-line macro definition should expand to a single physical line in output
HELLO_MSG("Player");

// Nested macro expansion + const usage
var _sum1 = ADD_MUL(2, 5, INT_LIT);
show_debug_message(_sum1);

// Member access should NOT be replaced (dot-prefixed identifier token)
var _col0 = ARR_REF[0]; // direct use is fine, ARR_REF should be static

// ============================
// FUNCTION SCOPE TESTS (const does not propagate into nested function bodies)
// ============================

function test_scope_outer(){
	// Outer function consts
	const LOC_NUM = 10
	const LOC_STR = @"raw_string"
	const LOC_FOLD = ADD(5, 5)

	// Inline uses inside the same function scope
	var _a = LOC_NUM + LOC_FOLD;
	var _b = LOC_STR + " ok";
	show_debug_message(_a);
	show_debug_message(_b);

	// Shadowing: inner const with same name is allowed, but must not affect outer
	function test_scope_inner(){
		const LOC_NUM = 999
		var _c = LOC_NUM + 1;
		show_debug_message(_c);
	}

	// Outer should still use outer LOC_NUM, not inner
	var _d = LOC_NUM + 1;
	show_debug_message(_d);

	// Call inner function
	test_scope_inner();
}

// Call scope test
test_scope_outer();

// ============================
// METHOD / FUNCTION-EXPR SCOPE TESTS (anonymous function bodies also isolate const)
// ============================

var method_func = function(){
	const METH_CONST = "method_scope"
	show_debug_message(METH_CONST);

	// Nested anonymous function - const must not propagate into it
	var _callback = function(){
		METH_CONST = "working!"
		// No const here - METH_CONST should NOT be replaced inside this nested function scope
		show_debug_message(METH_CONST);
	};

	_callback();
};

method_func();

// ============================
// TEMPLATE STRING TESTS (replacement should occur only in { } expressions)
// ============================

const TMP_A = 7
const TMP_B = ADD(1, 2)

var _tmpl = $"sum {TMP_A + TMP_B} done";
show_debug_message(_tmpl);
