# Security policy

Toji runs elevated and executes commands, so security reports are welcome.

**Please do not open a public issue for a vulnerability.** Use GitHub's
[private vulnerability reporting](https://github.com/ImSothe/Toji-Windows-Optimizer/security/advisories/new)
for this repository.

Useful to include: affected version, what an attacker needs (local user, network position, a malicious data file),
and a minimal reproduction. In scope: the updater and signature verification, the `%ProgramData%\SotheOptimizer`
trust model, command construction, and anything that makes a revert leave the system in a worse state than a clean
undo would. Out of scope: "the tool needs administrator rights" and "a user with administrator rights can do
harmful things".

You will get an answer as soon as I can; this is a one-person project.
