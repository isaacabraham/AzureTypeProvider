#r @"..\..\bin\netstandard2.0\publish\FSharp.Azure.StorageTypeProvider.dll"
#r @"..\..\packages\FSharp.Compiler.Tools\tools\netcoreapp1.0\FSharp.Build.dll"
open FSharp.Azure.StorageTypeProvider

// Get a handle to my local storage emulator
type Local = AzureTypeProvider<"UseDevelopmentStorage=true", "">

type BlobSchema = AzureTypeProvider<blobSchema = "BlobSchema.json">

let container = Local.Containers.samples

// Perform a strongly-typed query against a table with automatic schema generation.
let results =
    Azure.Tables.employee.Query()
         .``Where Name Is``.``Equal To``("fred")
         .Execute()
         |> Array.map(fun row -> row.Name, row.Dob)

// Navigate through storage queues and get messages
let queueMessage = Azure.Queues.``sample-queue``.Dequeue()