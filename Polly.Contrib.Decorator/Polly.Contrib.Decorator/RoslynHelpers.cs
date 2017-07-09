namespace Polly.Contrib.Decorator
{
    #region Using Directives

    using System.Collections.Generic;
    using System.Linq;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Editing;

    #endregion

    internal static class RoslynHelpers
    {
        #region Properties

        private static IEnumerable<string> PollyGenericTypeParameters
        {
            get
            {
                var pollyGenericTypeParameters = new[] { Constants.PollyGenericTypeParameter };
                return pollyGenericTypeParameters;
            }
        }

        #endregion

        #region Public Methods and Operators

        public static bool NamedItemExists(SyntaxNode classDeclaration, string identifierName)
        {
            return classDeclaration.DescendantNodes().
                OfType<MemberDeclarationSyntax>().
                SelectMany(m => m.DescendantTokens()).
                Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == identifierName);

            // TODO: Compare performance to :
            // if (!model.Compilation.ContainsSymbolsWithName(n => n == className))
        }

        #endregion

        #region Methods

        internal static SyntaxNode GenerateConstructorDeclaration(SyntaxGenerator gen, string className,
                                                                  string parameterName, string fieldName,
                                                                  ITypeSymbol fieldType)
        {
            var implementationParameter = gen.ParameterDeclaration(parameterName, gen.TypeExpression(fieldType));
            var pollyParameter = gen.ParameterDeclaration(Constants.PollyConstructorParameterName,
                gen.IdentifierName(Constants.PollyFieldType));

            var paramaters = new[] { implementationParameter, pollyParameter };
            var statements = new[]
                                 {
                                     gen.AssignmentStatement(gen.IdentifierName(fieldName),
                                         gen.IdentifierName(parameterName)),
                                     gen.AssignmentStatement(gen.IdentifierName(Constants.PollyFieldName),
                                         gen.IdentifierName(Constants.PollyConstructorParameterName))
                                 };
            return gen.ConstructorDeclaration(className, paramaters, statements: statements);
        }

        internal static SyntaxNode GenerateEvent(SyntaxGenerator gen, IEventSymbol eventSymbol)
        {
            return gen.EventDeclaration(eventSymbol.Name,
                gen.TypeExpression(eventSymbol.Type),
                Accessibility.Public,
                DeclarationModifiers.None);
        }

        internal static SyntaxNode GenerateFieldDeclaration(SyntaxGenerator gen, string fieldName, string fieldType)
        {
            return gen.FieldDeclaration(fieldName,
                gen.IdentifierName(fieldType),
                Accessibility.Private,
                DeclarationModifiers.ReadOnly);
        }

        internal static SyntaxNode GenerateMethodImplementation(SyntaxGenerator gen, SemanticModel model, int minificationLocation,
                                                                IMethodSymbol methodSymbol, string fieldName)
        {
            var parameters = methodSymbol.Parameters.Select(symbol => gen.ParameterDeclaration(symbol)).
                ToList();

            var arguments = methodSymbol.Parameters.Select(symbol => gen.Argument(gen.IdentifierName(symbol.Name))).
                ToList();

            var typeParameters = methodSymbol.TypeParameters.Select(symbol => (symbol.Name)).
                ToList();

            var typeArguments = methodSymbol.TypeArguments.Select(symbol => gen.IdentifierName(symbol.Name)).
                ToList();

            // TODO: Improve detection of potentially asynchronous methods
            var isAsync = false;

            var declarationModifier = DeclarationModifiers.None;
     
            if (methodSymbol.ReturnType.ToString().Contains("System.Threading.Tasks.Task"))
            {
                //declarationModifier = DeclarationModifiers.Async;
                isAsync = true;
            }

            var returnType = methodSymbol.ReturnsVoid
                                 ? null
                                 : methodSymbol.ReturnType.SpecialType == SpecialType.None
                                     ? gen.IdentifierName(methodSymbol.ReturnType.ToMinimalDisplayString(model,
                                         minificationLocation,
                                         SymbolDisplayFormat.MinimallyQualifiedFormat))
                                     : gen.TypeExpression(methodSymbol.ReturnType.SpecialType);

            var executionStatement = methodSymbol.ReturnsVoid ? GenerateLambdaInvocation(gen, methodSymbol, fieldName, typeArguments, arguments, isAsync)
                : GenerateLambdaReturn(gen, methodSymbol, fieldName, typeArguments, arguments, isAsync);

            var method = gen.MethodDeclaration(methodSymbol.Name,
                parameters,
                typeParameters,
                returnType,
                Accessibility.Public,
                declarationModifier,
                new[] { executionStatement });

            // Now we test to see if we have constraints
            var constraints = methodSymbol.TypeArguments.Select(symbol => symbol as ITypeParameterSymbol);

            foreach (var constraint in constraints)
            {
                if (constraint.HasConstructorConstraint)
                {
                    method = gen.WithTypeConstraint(method,
                        constraint.Name,
                        SpecialTypeConstraintKind.Constructor); // new()
                }

                if (constraint.HasReferenceTypeConstraint)
                {
                    method = gen.WithTypeConstraint(method,
                        constraint.Name,
                        SpecialTypeConstraintKind.ReferenceType); // class
                }

                if (constraint.HasValueTypeConstraint)
                {
                    method = gen.WithTypeConstraint(method,
                        constraint.Name,
                        SpecialTypeConstraintKind.ValueType); // struct
                }

                //TODO: Constraint names are not being minimised
                if (constraint.ConstraintTypes.Length > 0)
                {
                    method = gen.WithTypeConstraint(method,
                        constraint.Name,
                        SpecialTypeConstraintKind.None,
                        constraint.ConstraintTypes.Select(
                            symbol => gen.IdentifierName(symbol.ToMinimalDisplayString(model,
                                minificationLocation,
                                SymbolDisplayFormat.MinimallyQualifiedFormat))));
                }
            }

            return method;
        }

        private static SyntaxNode GenerateLambdaReturn(SyntaxGenerator gen, IMethodSymbol methodSymbol, string fieldName,
                                                       List<SyntaxNode> typeArguments, List<SyntaxNode> arguments, bool isAsync)
        {
            var pollyMethodName = isAsync? Constants.PollyMethodNameAsync : Constants.PollyMethodName;

            var lambdaReturn = gen.ReturnStatement(gen.InvocationExpression(gen.IdentifierName(pollyMethodName),
                gen.ValueReturningLambdaExpression(
                    gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(fieldName),
                            gen.GenericName(methodSymbol.Name, typeArguments)),
                        arguments))));

            //if (isAsync) { lambdaReturn = gen.AwaitExpression(lambdaReturn); }
            return lambdaReturn;
        }

        private static SyntaxNode GenerateLambdaInvocation(SyntaxGenerator gen, IMethodSymbol methodSymbol, string fieldName,
                                                           List<SyntaxNode> typeArguments, List<SyntaxNode> arguments, bool isAsync)
        {
            // Currently unable to detect void methods that implement GetAwaiter()
            //var pollyMethodName = isAsync ? Constants.PollyMethodNameVoidAsync : Constants.PollyMethodNameVoid;
            var pollyMethodName = Constants.PollyMethodNameVoid;

            var lambda = gen.InvocationExpression(gen.IdentifierName(pollyMethodName),
                gen.VoidReturningLambdaExpression(
                    gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(fieldName),
                            gen.GenericName(methodSymbol.Name, typeArguments)),
                        arguments)));

            //if (isAsync) { lambda = gen.AwaitExpression(lambda); }

            return lambda;
        }

        internal static void GenerateMissingEvents(ClassDeclarationSyntax classDeclaration, ITypeSymbol interfaceType,
                                                   ITypeSymbol classType, DocumentEditor editor, SyntaxGenerator gen)
        {
            foreach (var eventSymbol in interfaceType.GetMembers().
                OfType<IEventSymbol>())
            {
                if (classType.FindImplementationForInterfaceMember(eventSymbol) != null) continue;
                editor.AddMember(classDeclaration, GenerateEvent(gen, eventSymbol));
            }
        }

        internal static void GenerateMissingMethods(ClassDeclarationSyntax classDeclaration, ITypeSymbol interfaceType,
                                                    ITypeSymbol classType, DocumentEditor editor, SyntaxGenerator gen,
                                                    SemanticModel model, int minificationLocation)
        {
            foreach (var member in interfaceType.GetMembers().
                OfType<IMethodSymbol>().
                Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                if (classType.FindImplementationForInterfaceMember(member) != null) continue;

                editor.AddMember(classDeclaration,
                    GenerateMethodImplementation(gen, model, minificationLocation, member, Constants.ImplementationFieldName));
            }
        }

        internal static void GenerateMissingProperties(ClassDeclarationSyntax classDeclaration,
                                                       ITypeSymbol interfaceType, ITypeSymbol classType,
                                                       DocumentEditor editor, SyntaxGenerator gen)
        {
            foreach (var propertySymbol in interfaceType.GetMembers().
                OfType<IPropertySymbol>())
            {
                if (classType.FindImplementationForInterfaceMember(propertySymbol) != null) continue;
                editor.AddMember(classDeclaration,
                    GenerateProperty(gen, propertySymbol, Constants.ImplementationFieldName));
            }
        }

        internal static SyntaxNode GeneratePollyExecute(SyntaxGenerator gen, SyntaxNode[] pollyParameterName)
        {
            var parameters = new[]
                                 {
                                     gen.ParameterDeclaration(Constants.PollyParameterName,
                                         gen.GenericName(Constants.PollyFuncParameterName,
                                             gen.IdentifierName(Constants.PollyGenericTypeParameter)))
                                 };

            var returnType = gen.IdentifierName(Constants.PollyGenericTypeParameter);

            var statement =
                gen.ReturnStatement(
                    gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(Constants.PollyFieldName),
                            gen.IdentifierName("Execute")),
                        pollyParameterName));

            return gen.MethodDeclaration(Constants.PollyMethodName,
                parameters,
                PollyGenericTypeParameters,
                returnType,
                Accessibility.Private,
                DeclarationModifiers.None,
                new[] { statement });
        }

        internal static SyntaxNode GeneratePollyExecuteAsync(SyntaxGenerator gen, SyntaxNode[] polyFunc)
        {
            var parameters = new[]
                                 {
                                     gen.ParameterDeclaration(Constants.PollyParameterName,
                                         gen.GenericName(Constants.PollyFuncParameterName,
                                             gen.GenericName("Task",
                                                 gen.IdentifierName(Constants.PollyGenericTypeParameter))))
                                 };

            var returnType = gen.GenericName("Task", gen.IdentifierName(Constants.PollyGenericTypeParameter));

            //var statement =
            //    gen.ReturnStatement(gen.AwaitExpression(
            //        gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(Constants.PollyFieldName),
            //                gen.IdentifierName("ExecuteAsync")),
            //            polyFunc)));

            var statement =
                gen.ReturnStatement(
                    gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(Constants.PollyFieldName),
                            gen.IdentifierName("ExecuteAsync")),
                        polyFunc));

            return gen.MethodDeclaration(Constants.PollyMethodNameAsync,
                parameters,
                PollyGenericTypeParameters,
                returnType,
                Accessibility.Private,
                //DeclarationModifiers.Async,
                DeclarationModifiers.None,
                new[] { statement });
        }

        internal static SyntaxNode GeneratePollyExecuteVoid(SyntaxGenerator gen, SyntaxNode[] pollyParameterName)
        {
            var parameters = new[]
                                 {
                                     gen.ParameterDeclaration(Constants.PollyParameterName,
                                         gen.IdentifierName(Constants.PollyActionParameterName))
                                 };

            var statement =
                gen.InvocationExpression(gen.MemberAccessExpression(gen.IdentifierName(Constants.PollyFieldName),
                        gen.IdentifierName("Execute")),
                    pollyParameterName);

            return gen.MethodDeclaration(Constants.PollyMethodNameVoid,
                parameters,
                null,
                null,
                Accessibility.Private,
                DeclarationModifiers.None,
                new[] { statement });
        }

        internal static SyntaxNode GenerateProperty(SyntaxGenerator gen, IPropertySymbol propertySymbol,
                                                    string implementationFieldName)
        {
            IEnumerable<SyntaxNode> getAccessorStatements =
                new[]
                    {
                        gen.ReturnStatement(gen.MemberAccessExpression(gen.IdentifierName(implementationFieldName),
                            propertySymbol.Name))
                    };

            IEnumerable<SyntaxNode> setAccessorStatements = null;

            if (!propertySymbol.IsReadOnly)
            {
                setAccessorStatements = new[]
                                            {
                                                gen.AssignmentStatement(
                                                    gen.DottedName(
                                                        implementationFieldName + "." + propertySymbol.Name),
                                                    gen.IdentifierName("value"))
                                            };
            }

            return gen.PropertyDeclaration(propertySymbol.Name,
                gen.TypeExpression(propertySymbol.Type),
                Accessibility.Public,
                DeclarationModifiers.None,
                getAccessorStatements,
                setAccessorStatements);
        }

        #endregion
    }
}