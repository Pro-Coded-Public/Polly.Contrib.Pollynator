# Polly.Contrib.Decorator

An initial sample Visual Studio CodeFix that implements an interface with all calls to properties and methods executed via Polly.

To use, clone the repo, and make sure you have Polly.Contrib.Decorator.Vsix set as the startup project. 
Running the project will start a clean instance of Visual Studio, with the CodeFix installed. 
You can then create a class that is intended to implement an interface, and Visual Studion will flag that this interace is not implemented.
One of the options to automatically implement the interface, is 'Implement Interface Decorated with Polly'

To test this extension, a sample project containing classes with unimplemented interfaces (e.g. Redux, DocumentDB Client) is available here:

https://github.com/Pro-Coded/Polly.Contrib.Decorator.Sample

