# Project workflow

## Verification boundaries

- Verify code by letting the open Unity Editor import and compile it cleanly.
- Writing and running automated tests is allowed and encouraged when it is useful.
- Do not create standalone/player builds as routine verification. Build only when the user explicitly asks for one.
- Leave manual checks, including Play Mode checks, to the developer by default. Enter Play Mode only when the user explicitly asks or when a narrowly scoped check is essential and cannot be covered by compilation or automated tests.
- When handing work off, report the compilation and automated-test results, and list any remaining manual checks for the developer instead of performing them.
