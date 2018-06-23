# DriverInteropLibrary overview

The DriverInteropLibrary DLL provides functionality to talk to the IPv6ToBle.sys driver, both synchronously and asynchronously. The code for this library is based off of Jeffrey Richter's seminal articles in the March 2007 and June 2007 issues of MSDN magazine, but is modified to fit the needs of this project.

## General info and purpose

Communication between a user mode C# app and a kernel mode driver is possible by using the Platform Invoke, or P/Invoke, library provided by Microsoft. P/Invoke permits a C# app to import native code functions while also providing functions to marshal data and pointers between user mode and kernel mode.

To talk to the driver, several functions are imported from native code. The first, [CreateFile](https://msdn.microsoft.com/library/windows/desktop/aa363858), opens a handle to the driver. The second, [CloseFile](https://msdn.microsoft.com/library/windows/desktop/aa363858), closes the handle. The third, [DeviceIoControl](https://msdn.microsoft.com/library/windows/desktop/aa363216) is what sends a given command code with input and output buffers to the driver.

These three functions, combined with the I/O Control Codes (IOCTLs) defined in the driver, are the foundation for app to driver communication. Using them, synchronous operations are quite easy. However, asynchronous operations require a manual implementation of the C# asynchronous programming model to properly execute. That is the bulk of the code in this library.

## Classes

- AsyncResultObjects
    - AsyncResultNoResult.cs
        - An implementation of the [IAsyncResult](https://msdn.microsoft.com/library/system.iasyncresult) interface that represents the results of an asynchronous operation. Does not permit returning a value.
    - AsyncResult.cs
        - Extends the AsyncResultNoResult class to permit returning a generic value.
    - DeviceAsyncResult.cs
        - Extends the AsyncResult class for performing asynchronous operations with the driver. Uses the [NativeOverlapped](https://msdn.microsoft.com/library/system.threading.nativeoverlapped) structure to pass to native code for async calls.
- DeviceIO
    - DeviceIO.cs
        - Contains the public-facing calls for this library to open a handle to the driver and perform both synchronous and asynchronous operations.
    - SafePinnedObject.cs
        - A wrapper object that pins a managed object/buffer in memory so the garbage collector doesn't move it around while an asynchronous operation completes.
- IPv6ToBleIoctl.cs
    - Contains I/O control code (IOCTL) definitions that are identical to the ones defined in Public.h in the driver.
- Kernel32Import
    - Contains P/Invoke native function imports for opening/closing a handle to the driver and sending the driver commands.