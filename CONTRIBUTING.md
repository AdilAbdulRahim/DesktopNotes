# Contributing to DesktopNotes

Thanks for checking out DesktopNotes! This is a learning project, and contributions, questions, bug reports, and ideas are welcome. You do not need to be an expert to get involved.

## Ways to contribute

- Report a bug or suggest a feature through the repository's issue templates.
- Improve the documentation or share feedback.
- Submit a code change as a pull request.

Before opening an issue, please check whether someone has already reported the same problem. Bug reports are most helpful when they include steps to reproduce the issue and relevant screenshots or logs. For feature ideas, explain who the change would help and why.

## Submitting a change

1. Fork the repository and create a branch for your change.
2. Make a focused change that follows the existing C# naming and file-layout conventions.
3. Build the solution:

   ```powershell
   dotnet build DesktopNotes.sln -c Release
   ```

4. Open a pull request against `main`. Describe what you changed, why you made the change, and how you checked it. Include screenshots for user-facing changes when helpful.

## Pull request checklist

- [ ] The change is focused and follows the existing project conventions.
- [ ] The solution builds successfully.
- [ ] Relevant tests pass, if tests are available for the change.
- [ ] The pull request explains the change and how it was verified.

There is no expectation that every contribution be perfect on the first try. I appreciate the effort, and I am happy to discuss feedback during review.

## Security issues

Please do not report security vulnerabilities in public issues. See [SECURITY.md](./SECURITY.md) for private reporting instructions.

Thanks for helping improve DesktopNotes!
