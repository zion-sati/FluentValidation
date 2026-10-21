# NetWasm.FluentValidation

FluentValidation 12.2.0 built for NetWasm's `netwasm0.1` target. C# source
continues to use the `FluentValidation` namespace.

Construct validators explicitly and register them explicitly. Reflection-based
assembly scanning, type-based validator factories, and automatic validator
discovery are not included in this package.

`NetWasm.FluentValidation` 0.10.0 uses NetWasm SDK 0.10.0 and the 0.10.0 ported
library packages. These versions need not be identical: use the supported
dependency versions rather than changing all packages to a compiler patch version.

The current profile does not support non-invariant cultures, expression member
access on struct receivers, or expression boxing conversions. Ordinary instance
fields on reference-type receivers are supported. These expression restrictions
apply to accessor lambdas passed to validation rules, not to ordinary compiled
C# field access or boxing. See NetWasm's [expression-tree profile](https://github.com/zion-sati/NetWasm/blob/main/docs/expression-trees.md).
