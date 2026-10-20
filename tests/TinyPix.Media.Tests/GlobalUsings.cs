global using Xunit;

// Native ImageMagick (Magick.NET) is not reliably thread-safe under the
// concurrent access xUnit applies across test classes by default: the image
// workbench suite and the code suite both drive it, and parallel classes
// intermittently corrupt pixel extraction. The whole suite runs in seconds,
// so serialise it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
