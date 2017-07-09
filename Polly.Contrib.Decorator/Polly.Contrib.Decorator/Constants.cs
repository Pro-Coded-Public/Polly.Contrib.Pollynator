namespace Polly.Contrib.Decorator
{
    internal static class Constants
    {
        #region Constants

        internal const string CS0535 = nameof(CS0535)
            ; // 'Program' does not implement interface member 'System.Collections.IEnumerable.GetEnumerator()'
        internal const string CS0737 = nameof(CS0737)
            ; // 'Class' does not implement interface member 'IInterface.M()'. 'Class.M()' cannot implement an interface member because it is not internal.
        internal const string CS0738 = nameof(CS0738)
            ; // 'C' does not implement interface member 'I.Method1()'. 'B.Method1()' cannot implement 'I.Method1()' because it does not have the matching return type of 'void'.
        internal const string ImplementationFieldName = "_wrappedImplemention";
        internal const string ImplementationParameterName = "implementation";
        internal const string PollyMethodNameAsync = "PollyExecuteAsync";
        internal const string PollyParameterName = "method";
        internal const string PollyGenericTypeParameter = "T";
        internal const string PollyFuncParameterName = "Func";
        internal const string PollyActionParameterName = "Action";

        internal const string PollyMethodName = "PollyExecute";
        internal const string PollyMethodNameVoid = "PollyExecuteVoid";
        //internal const string PollyMethodNameVoidAsync = "PollyExecuteVoidAsync"; // Currently unable to detect void methods that implement GetAwaiter()

        internal const string PollyFieldName = "_polly";
        internal const string PollyFieldType = "PolicyWrap";
        internal const string PollyConstructorParameterName = "polly";

        internal const string Title = "Implement Interface Decorated with Polly";

        #endregion
    }
}