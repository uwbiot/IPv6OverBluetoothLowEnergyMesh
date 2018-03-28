/*++

Module Name:

	Helpers_NetBuffer.c

Abstract:

	This file contains the implementations for helper functions to work with
	NET_BUFFER_LIST and NET_BUFFER structures.

Environment:

	Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "Helpers_NetBuffer.tmh"    // auto-generated tracing file

_Use_decl_annotations_
NET_BUFFER_LIST*
IPv6ToBleNBLCreateFromBuffer(
	_In_							    NDIS_HANDLE	nblPoolHandle,
	_In_reads_(*packetFromUsermodeSize) BYTE*		packetFromUsermode,
	_In_							    size_t*		packetFromUsermodeSize
)
/*++
Routine Description:

	Creates a NET_BUFFER_LIST (NBL), the standard structure for kernel-mode
	network	I/O, from a byte array input buffer (passed from usermode).

	This function is called by the EvtIoControl callback if the IOCTL to send
	out a packet is received.

	This function is heavily based on the "KrnlHlprNBLCreateFromBuffer" helper
	function in the WFPSAMPLER sample driver from Microsoft.

Arguments:

	nblPoolHandle - a handle to the pool from which to allocate the NBL. This
	was passed from an NDIS_POOL_DATA structure that resides in the device
	context space. That structure was allocated and prepared during device
	creation.

	packetFromUsermode - the byte array passed from usermode. This is a
	complete packet that already has the IP header at the front, as required
	by the outbound packet injection functions.

	packetFromUsermodeSize - the size of the buffer.

Return Value:

	Returns a pointer to the NBL if successful, NULL otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_NET_BUFFER, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

	PMDL memoryDescriptorList = 0;	// The MDL used to initialize the NBL
	NET_BUFFER_LIST* NBL = 0;		// The NBL to allocate and return

	//
	// Step 1
	// Allocate and build the MDL from the buffer
	//
	memoryDescriptorList = IoAllocateMdl(packetFromUsermode,
		                                 (ULONG)&packetFromUsermodeSize,
		                                 FALSE,
		                                 FALSE,
		                                 0
	                                     );
	if (!memoryDescriptorList)
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_NET_BUFFER, "Allocating MDL from usermode packet failed");
		goto Exit;
	}
	MmBuildMdlForNonPagedPool(memoryDescriptorList);

	//
	// Step 2
	// Allocate the NET_BUFFER_LIST and its single child NET_BUFFER
	//
	status = FwpsAllocateNetBufferAndNetBufferList0(nblPoolHandle,
		                                            0,
		                                            0,
		                                            memoryDescriptorList,
		                                            0,
		                                            *packetFromUsermodeSize,
		                                            &NBL
	                                                );

Exit:
	
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_NET_BUFFER, "Allocating NBL failed %!STATUS!", status);
		IoFreeMdl(memoryDescriptorList);
		memoryDescriptorList = 0;
	}

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_NET_BUFFER, "%!FUNC! Exit");

	return NBL;
}

_Use_decl_annotations_
BYTE*
IPv6ToBleNBLCopyToBuffer(
	_In_	NET_BUFFER_LIST*	NBL,
	_Out_	UINT32*				size,
	_In_	UINT32				additionalSpace
)
/*++
Routine Description:

	Copies a NET_BUFFER_LIST (NBL), the standard structure for kernel-mode
	network	I/O, to a byte array input buffer (to be passed to usermode).

	This function is called by the classifyFn when it receives an incoming
	packet that it wants to pass to user mode. 
	
	When the classify callout is registered at the IP_PACKET_V6 layer of the 
	TCP/IP stack, the NBL that is passed to the classifyFn represents a 
	complete IP packet. The IP header has been parsed and the NBL starts 
	immediately after the IP header. Therefore, this function needs to
	"retreat" the NBL to reclaim the IP header (re-advancing later before
	returning).

	This function is heavily based on the "KrnlHlprNBLCopyToBuffer" helper
	function in the WFPSAMPLER sample driver from Microsoft.

Arguments:

	NBL - the NET_BUFFER_LIST to copy to the buffer.

	size - the number of bytes copied to the array.

	additionalSpace - any additional space needed in the buffer. In this case,
	this is the IP header because the NBL passed in starts *after* the IP
	header. This value is acquired during the classifyFn where the driver can
	query the length of the IP header.

Return Value:

	Returns a pointer to the byte array if successful, NULL if not.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_NET_BUFFER, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

	//
	// Step 1
	// Prepare information for copying the bytes (num bytes to copy, num bytes
	// copied, etc.)
	//	
	BYTE* packetForUsermode = 0;
	UINT32 bytesToCopy = additionalSpace;
	*size = 0;

	//
	// Step 2
	// Retreat the NBL to "reclaim" the IP header (assuming sizeof(IP header)
    // was passed in to the additional space parameter)
	// 
	NDIS_STATUS ndisStatus = NDIS_STATUS_SUCCESS;
	ndisStatus = NdisRetreatNetBufferListDataStart(NBL,
												   additionalSpace,
												   0, 
												   NULL,
												   NULL
											   	   );
	if (ndisStatus != NDIS_STATUS_SUCCESS)
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_NET_BUFFER, "Retreating NBL failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

	//
	// Step 3
	// Determine the number of bytes to copy by traversing the NET_BUFFER
	// structures that are the children of the NET_BUFFER_LIST. Each NBL can
	// have 1 or more NB children. 
	// 
	// Of note is that when the Windows Filtering Platform gives an NBL to the
	// driver, representing a packet, the NBL has exactly one child NB.
	//
	if (NBL) 
	{
		for (NET_BUFFER* netBuffer = NET_BUFFER_LIST_FIRST_NB(NBL);
			netBuffer;
			netBuffer = NET_BUFFER_NEXT_NB(netBuffer))
		{
			bytesToCopy += NET_BUFFER_DATA_LENGTH(netBuffer);
		}
	}

    //
    // Step 4
    // Create a new array of zeroes for the packet for user mode
    //
    for (; packetForUsermode == 0;)
    {
        size_t SAFE_SIZE = 0;
        if (bytesToCopy &&
            RtlSizeTMult(sizeof(BYTE),
                        (size_t)bytesToCopy,
                        &SAFE_SIZE) == STATUS_SUCCESS &&
            SAFE_SIZE >= (sizeof(BYTE) * bytesToCopy))
        {
            packetForUsermode = (BYTE*)ExAllocatePoolWithTag(NonPagedPoolNx,
                                    SAFE_SIZE,
                                    IPV6_TO_BLE_NDIS_TAG
                                );
            if (packetForUsermode)
            {
                RtlZeroMemory(packetForUsermode, SAFE_SIZE);
            }
        }
        else
        {
            packetForUsermode = 0;
            break;
        }
    }
    if (packetForUsermode == 0)
    {
        status = (UINT32)STATUS_NO_MEMORY;
        goto Exit;
    }

	//
	// Step 5
	// Copy the data to the byte array
	//
	if (NBL)
	{
		NET_BUFFER* netBuffer = NET_BUFFER_LIST_FIRST_NB(NBL);

		// Copy the data in each NET_BUFFER
		for (UINT32 bytesCopied = 0;
			bytesCopied < bytesToCopy && netBuffer;
			netBuffer = NET_BUFFER_NEXT_NB(netBuffer))
		{
			BYTE* contiguousBuffer = 0;
			BYTE* allocatedBuffer = 0;
			UINT32 bytesNeeded = NET_BUFFER_DATA_LENGTH(netBuffer);

			if (bytesNeeded)
			{
				// Allocate a buffer of zeroes to hold the data from the
				// NET_BUFFER
				allocatedBuffer = (BYTE*)ExAllocatePoolWithTag(NonPagedPoolNx,
										(sizeof(BYTE) * bytesNeeded),
										IPV6_TO_BLE_NBL_TAG
									);
				if (!allocatedBuffer)
				{
					status = (UINT32)STATUS_NO_MEMORY;
                    TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_NET_BUFFER, "Memory allocation for NET_BUFFER data retrieval failed %!STATUS!", status);
					goto Exit;
				}

				// Get the data from the NET_BUFFER
				contiguousBuffer = (BYTE*)NdisGetDataBuffer(netBuffer,
															bytesNeeded,
															allocatedBuffer,
															1, 
															0
															);
				
				// Copy the memory from this NET_BUFFER to the byte array to
				// return (i.e. the packet to pass to usermode)
				RtlCopyMemory(&packetForUsermode[bytesCopied],
					contiguousBuffer ? contiguousBuffer : allocatedBuffer,
					bytesNeeded
				);

				bytesCopied += bytesNeeded;

				// Delete the allocated buffer
				if (allocatedBuffer)
				{
					ExFreePoolWithTag((BYTE*)allocatedBuffer, 
										IPV6_TO_BLE_NBL_TAG
										);
					allocatedBuffer = 0;
				}
			}
		}
	}

Exit:
	
	// Advance the NBL to undo the retreat we did earlier, if the retreat was
	// successful
	if (ndisStatus == NDIS_STATUS_SUCCESS) 
	{
		NdisAdvanceNetBufferListDataStart(NBL,
			                              additionalSpace,
			                              FALSE,
			                              NULL
		                                  );
	}

	// Free the packet if we failed, else report the size and return it
	if (!NT_SUCCESS(status))
	{
		if (packetForUsermode)
		{
			ExFreePoolWithTag((BYTE*)packetForUsermode,
								IPV6_TO_BLE_NBL_TAG
								);
			packetForUsermode = 0;
		}
	}
	else 
	{
		*size = bytesToCopy;
	}

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_NET_BUFFER, "%!FUNC! Exit");

	return packetForUsermode;
}