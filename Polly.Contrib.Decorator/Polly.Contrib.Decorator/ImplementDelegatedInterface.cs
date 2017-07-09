namespace Polly.Contrib.Decorator
{
    #region Using Directives

    using System.Collections.Immutable;
    using System.Composition;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Editing;

    #endregion

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = Constants.Title)]
    [Shared]
    public class ImplementDelegatedInterface : CodeFixProvider
    {
        #region Public Properties

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(Constants.CS0535,
            Constants.CS0737,
            Constants.CS0738);

        #endregion

        #region Public Methods and Operators

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // Gather all required information before registering as a CodeFix
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).
                           ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var classDeclaration = root.FindToken(diagnostic.Location.SourceSpan.Start).
                Parent.AncestorsAndSelf().
                OfType<ClassDeclarationSyntax>().
                First();

            // Find the interface Syntax Token detected by the diagnostic.
            var interfaceIdentifier = root.FindToken(diagnosticSpan.Start).
                Parent.AncestorsAndSelf().
                OfType<SimpleBaseTypeSyntax>().
                First().
                DescendantNodes().
                First();

            if (interfaceIdentifier is null) return;

            var minificationLocation = classDeclaration.SpanStart;

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(CodeAction.Create(Constants.Title,
                    c => FixImplementDelegatedInterfaceAsync(context.Document,
                        interfaceIdentifier,
                        c,
                        classDeclaration,
                        minificationLocation),
                    equivalenceKey: Constants.Title),
                diagnostic);
        }

        #endregion

        #region Methods

        private static async Task<Document> FixImplementDelegatedInterfaceAsync(
            Document document, SyntaxNode interfaceIdentifier, CancellationToken cancellationToken,
            ClassDeclarationSyntax classDeclaration, int minificationLocation)
        {
            // Create a DocumentEditor instance
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).
                             ConfigureAwait(false);
            var model = editor.SemanticModel; // We use the SemanticModel to obtain Type information            
            var generator = editor.Generator; // We use the SyntaxGenerator to create new code elements

            var className = classDeclaration.Identifier.ValueText;
            var classType = model.GetDeclaredSymbol(classDeclaration) as ITypeSymbol;
            var interfaceTypeInfo = model.GetTypeInfo(interfaceIdentifier.FirstAncestorOrSelf<IdentifierNameSyntax>());
            var interfaceType = interfaceTypeInfo.Type;
           
            if (!RoslynHelpers.NamedItemExists(classDeclaration, Constants.ImplementationFieldName))
            {
                editor.AddMember(classDeclaration,
                    RoslynHelpers.GenerateFieldDeclaration(generator,
                        Constants.ImplementationFieldName,
                        interfaceType.Name));
            }

            if (!RoslynHelpers.NamedItemExists(classDeclaration, Constants.PollyFieldName))
            {
                editor.AddMember(classDeclaration,
                    RoslynHelpers.GenerateFieldDeclaration(generator,
                        Constants.PollyFieldName,
                        Constants.PollyFieldType));
            }

            if (!RoslynHelpers.NamedItemExists(classDeclaration, className))
            {
                var constructorDeclaration = RoslynHelpers.GenerateConstructorDeclaration(generator,
                    className,
                    Constants.ImplementationParameterName,
                    Constants.ImplementationFieldName,
                    interfaceType);

                editor.AddMember(classDeclaration, constructorDeclaration);
            }

            var pollyParameterName = new[] { generator.IdentifierName(Constants.PollyParameterName) };

            if (!RoslynHelpers.NamedItemExists(classDeclaration, Constants.PollyMethodNameVoid))
            {
                editor.AddMember(classDeclaration,
                    RoslynHelpers.GeneratePollyExecuteVoid(generator, pollyParameterName));
            }

            if (!RoslynHelpers.NamedItemExists(classDeclaration, Constants.PollyMethodName))
            {
                editor.AddMember(classDeclaration, RoslynHelpers.GeneratePollyExecute(generator, pollyParameterName));
            }

            if (!RoslynHelpers.NamedItemExists(classDeclaration, Constants.PollyMethodNameAsync))
            {
                editor.AddMember(classDeclaration,
                    RoslynHelpers.GeneratePollyExecuteAsync(generator, pollyParameterName));
            }

            RoslynHelpers.GenerateMissingEvents(classDeclaration, interfaceType, classType, editor, generator);

            RoslynHelpers.GenerateMissingProperties(classDeclaration, interfaceType, classType, editor, generator);

            RoslynHelpers.GenerateMissingMethods(classDeclaration,
                interfaceType,
                classType,
                editor,
                generator,
                model,
                minificationLocation);

            return editor.GetChangedDocument();
        }

        #endregion
    }
}