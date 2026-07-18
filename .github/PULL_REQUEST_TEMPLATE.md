<!--
Thanks for contributing! A couple of things that make a PR easy to say yes to:
- For anything non-trivial, link an issue where the approach was discussed first.
- Keep it small and focused. New logic in Liveolator.Core should include tests.
-->

## What does this change?

<!-- A short description of the change and why. -->

## Related issue

<!-- e.g. Closes #123. For non-trivial work, please link the discussion issue. -->

## Checklist

- [ ] `dotnet test Liveolator.sln` passes locally
- [ ] New logic in `Liveolator.Core` is covered by tests
- [ ] The change keeps the seam intact (inputs emit `PerformanceAction`s; engines are driven through the dispatcher)
- [ ] I have the right to license this under GPLv3+ (no proprietary / GPL-incompatible code)
