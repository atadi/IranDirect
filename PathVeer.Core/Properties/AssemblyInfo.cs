// Expose internal persistence-test seams (the internal JsonStore policy
// constructor, JsonStoreRecoveryOptions, IJsonStoreFileOperations) to the
// in-repo test assembly so the policy contract can be exercised without
// widening the PUBLIC constructor surface. No external caller gains access.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PathVeer.Core.Tests")]
