﻿(*** hide ***)
#load @"..\tools\references.fsx"
open Deedle
open FSharp.Azure.StorageTypeProvider
open FSharp.Azure.StorageTypeProvider.Table
open Microsoft.WindowsAzure.Storage.Table
open System

type Azure = AzureTypeProvider<"UseDevelopmentStorage=true">

(**
Working with Tables
===================

For more information on Tables in general, please see some of the many articles on
[MSDN](https://msdn.microsoft.com/en-us/library/microsoft.windowsazure.storage.table.aspx) or the [Azure](http://azure.microsoft.com/en-us/documentation/services/storage/) [documentation](http://azure.microsoft.com/en-us/documentation/articles/storage-dotnet-how-to-use-tables/). Some of the core features of the Tables provider are: -

##Rapid navigation

You can easily move between tables within a storage account. Simply dotting into a Tables property
will automatically retrieve the list of tables in the storage account. This allows
easy exploration of your table assets, directly from within the REPL.
*)

(*** define-output: tableStats ***)
let employeeTable = Azure.Tables.employee
printfn "The table is called '%s'." employeeTable.Name
(*** include-output: tableStats ***)

(**

##Automatic schema inference
Unlike some non-relational data stores, Azure Table Storage does maintain schema information at the
row level in the form of EDM metadata. This can be interrogated in order to infer a table's shape,
as well as help query data. Let's look at the schema of the row ``fred.1`` in the ``employee``
table.
*)

(*** hide ***)

let theData =
    let keys = [ "Column Name"; "EDM Data Type"; "Value" ]
    let series = 
        [ [ "Partition Key"; "Row Key"; "Years Worked"; "Dob"; "Name"; "Salary"; "Is Manager" ]
          [ "string"; "string"; "int"; "datetime"; "string"; "double"; "bool" ]
          [ "fred"; "1"; "10"; "01/05/1990"; "fred"; "0"; "true"] ]
        |> List.map Series.ofValues
        |> List.map (fun s -> s :> ISeries<_>)
    Frame(keys, series) |> Frame.indexRowsString "Column Name"

(*** include-value: theData ***)

(**
Based on this EDM metadata, an appropriate .NET type can be generated and used within the provider.
*)

(*** hide ***)

let fred = employeeTable.Get(Row "1", Partition "men").Value

(** *)

(*** define-output: fred ***)
printfn "Fred has Partition Key of %s and Row Key of %s" fred.PartitionKey fred.RowKey
printfn "Fred has Name '%s', Years Working '%d', Dob '%O', Salary '%f' and Is Manager '%b'."
    fred.Name fred.YearsWorking fred.Dob fred.Salary fred.IsManager
(*** include-output: fred ***)

(** 
In addition, an extra "Values" property is available which exposes all properties on the entity
in a key/value collection - this is useful for binding scenarios or e.g. mapping in Deedle frames.
*)

let frame =
    "women"
    |> employeeTable.GetPartition
    |> Frame.ofRecords
    |> Frame.expandCols [ "Values" ] // expand the "Values" nested column
    |> Frame.indexRowsUsing(fun c -> sprintf "%O.%O" c.["PartitionKey"] c.["RowKey"]) // Generate a useful row index
    |> Frame.dropCol "PartitionKey"
    |> Frame.dropCol "RowKey"
    |> fun f -> f |> Frame.indexColsWith (f.ColumnKeys |> Seq.map(fun c -> c.Replace("Values.", ""))) // rename columns to omit "Values."
(*** include-value: frame ***)

(**

##Querying data
The storage provider has an easy-to-use query API that is also flexble and powerful, and uses the 
inferred schema to generate the appropriate query functionality. Data can be queried in several
ways: -

###Key Lookups
These are the simplest (and best performing) queries, based on a partition / row key combination,
returning an optional result. You can also retrieve an entire partition.
*)
let sara = employeeTable.Get(Row "1", Partition "women") // try to get a single row
let allWomen = employeeTable.GetPartition("women") // get all rows in the "women" partition

(**
###Plain Text Queries
If you need to search for a set of entities, you can enter a plain text search, either manually or
using the Azure SDK query builder.

*)
(*** define-output: query2 ***)
// create the query string by hand
let someData = employeeTable.Query("YearsWorking eq 35")
printfn "The query returned %d rows." someData.Length

// generate a query string programmatically.
let filterQuery =
    TableQuery.GenerateFilterConditionForInt("YearsWorking", QueryComparisons.Equal, 35)
printfn "Generated query is '%s'" filterQuery
(*** include-output: query2 ***)

(**

###Query DSL
A third alternative to querying with the Azure SDK is to use the LINQ IQueryable implementation.
This works in F# as well using the ``query { }`` computation expression. However, this
implementation has two main limitations: -
    - You need to manually create a type to handle the result
    - IQueryable does not guarantee runtime safety for a query that compiles. This is particularly
    evident with the Azure Storage Tables, which allow a very limited set of queries.

The Table provider allows an alternative that has the same sort of composability of IQueryable, yet
is strongly typed at compile- and runtime, whilst being extremely easy to use. It generates query
methods appropriate for each property on the entity, including appropriate clauses e.g. Greater
Than, Less Than etc. as supported by the Azure Storage Table service *)

let tpQuery = employeeTable.Query().``Where Years Working Is``.``Equal To``(35)

(*** include-value: tpQuery ***)

(** These can be composed and chained. When you have completed building, simply call ``Execute()``.
*)

let longerQuery = employeeTable.Query()
                       .``Where Years Working Is``.``Greater Than``(14)
                       .``Where Name Is``.``Equal To``("Fred")
                       .``Where Is Manager Is``.True()

(*** include-value: longerQuery ***)

(**
Query operators are strongly typed, so ``Equal To`` on ``Years Working`` takes in an int, whereas
on ``Name`` it takes in a string, whilst for booleans there is no such provided method.

##Inserting data
Inserting data is extremely easy with Azure Type Provider. A table will always have an Insert
method on it, with various overloads that will appear depending on whether the table has data in
it (and thus a schema could be inferred) or is empty.

###Inserting single records
If the table is newly-created (and has no existing data from which to infer schema), you can insert
data by calling one of the two available Insert overloads. The first one takes a single
PartitionKey, RowKey and any object. It will automatically generate the appropriate Azure Storage
request for all public properties. Conversely, if the table exists and has schema associated with
it, you can use a strongly-typed Insert method. You can also choose whether to insert or upsert
(insert or update) data.

Both mechanisms will also return a TableResponse discriminated union on the outcome of the operation.

*)

type Person = { Name : string; City : string }
let emptyTable = Azure.Tables.emptytable
// insert a single row into an empty table
let response = emptyTable.Insert(Partition "Europe", Row "1", { Name = "Isaac"; City = "London" })

(*** include-value: response ***)
let newEmployee =
    new Azure.Domain.employeeEntity(
        Partition "women", Row "3",
        Dob = DateTime(1979, 2, 12),
        IsManager = true,
        Name = "sophie",
        Salary = 123.,
        YearsWorking = 17)
let newEmployeeResponse = employeeTable.Insert(newEmployee)

(*** include-value: newEmployeeResponse ***)

let upsertResponse = employeeTable.Insert(newEmployee, TableInsertMode.Upsert)

(*** include-value: upsertResponse ***)

(** 

###Inserting batches
The Storage provider makes it easier to insert large amounts of data by automatically grouping
large datasets into the appropriate batches, which for Azure Tables have to be across the same
partition and in sizes of up to 100 entities. You can insert batches for tables that either
already have schema or do not.

*)

let people = [ Partition "Europe", Row "2", { Name = "Tony"; City = "Milan"}
               Partition "North America", Row "1", { Name = "Sally"; City = "New York"}
               Partition "Europe", Row "3", { Name = "Dirk"; City = "Frankfurt"} ]

// results are grouped by the batch that they were inserted in.
let batchResults = emptyTable.Insert(people)

(*** include-value: batchResults ***)

(**
##Deleting data
Delete data is also extremely easy - simply supply the set of Partition / Row Key combinations that
you wish to delete. Unfortunately there is no way to delete an entire partition - this is a limitation
of the Table Storage service 
*)

let deleteResult = emptyTable.Delete [ for row in 1 .. 3 -> Partition "Europe", Row (row.ToString()) ]

(*** include-value: deleteResult ***)

(**
##Error handling
Operations should not raise exceptions. Instead, return codes of any write operations return a
TableResponse. This contains the Partition and Row Keys of the affected row as well as the
associated TableResult, wrapped in a discriminated union to allow easy matching.

*)

let handleResponse =
    function
    | SuccessfulResponse(entityId, errorCode) -> printfn "Entity %A succeeded: %d." entityId errorCode
    | EntityError(entityId, httpCode, errorCode) -> printfn "Entity %A failed: %d - %s." entityId httpCode errorCode
    | BatchOperationFailedError(entityId) -> printfn "Entity %A was ignored as part of a failed batch operation." entityId
    | BatchError(entityId, httpCode, errorCode) -> printfn "Entity %A failed with an unknown batch error: %d - %s." entityId httpCode errorCode

(*** define-output: singleFailure ***)
// Insert an existing entity
employeeTable.Insert(newEmployee) |> handleResponse
(*** include-output: singleFailure ***)

(*** define-output: batchFailure ***)
// Insert the same batch twice
emptyTable.Insert(people)
emptyTable.Insert(people) |> Seq.collect snd |> Seq.iter handleResponse
(*** include-output: batchFailure ***)

(*** define-output: deleteFailure ***)
// Delete a non-existant entity
emptyTable.Delete([ Partition "Foo", Row "Bar" ]) |> Seq.collect snd |> Seq.iter handleResponse

(*** include-output: deleteFailure ***)