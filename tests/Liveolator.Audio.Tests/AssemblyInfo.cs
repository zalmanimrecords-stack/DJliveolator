using Xunit;

// BASS keeps GLOBAL native state — one init and one device shared by the whole process — so two test
// classes that touch it concurrently init and free it under each other, and the loser decodes silence.
// That showed up as the stretch/ramp render tests failing in whichever combination happened to run
// together, and passing when re-run alone. Same reason Liveolator.App.Tests serializes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
