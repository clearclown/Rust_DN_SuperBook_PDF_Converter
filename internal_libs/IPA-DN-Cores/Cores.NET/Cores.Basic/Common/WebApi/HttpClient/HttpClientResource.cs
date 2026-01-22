// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;

// Some parts of this program are from Microsoft CoreCLR - https://github.com/dotnet/coreclr
// 
// The MIT License (MIT)
// 
// Copyright (c) .NET Foundation and Contributors
// 
// All rights reserved.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace IPA.Cores.Basic.HttpClientCore;

static class SR
{
    public const string ArgumentOutOfRange_FileLengthTooBig‎ = "Specified file length was too large for the file system.";
    public const string ArgumentOutOfRange_NeedNonNegativeNum‎ = "Non-negative number required.";
    public const string ArgumentOutOfRange_NeedPosNum‎ = "Positive number required.";
    public const string event_OperationReturnedSomething‎ = "{0} returned {1}.";
    public const string InvalidHeaderName‎ = "An invalid character was found in header name.";
    public const string IO_FileExists_Name‎ = "The file '{0}' already exists.";
    public const string IO_FileNotFound‎ = "Unable to find the specified file.";
    public const string IO_FileNotFound_FileName‎ = "Could not find file '{0}'.";
    public const string IO_PathNotFound_NoPathName‎ = "Could not find a part of the path.";
    public const string IO_PathNotFound_Path‎ = "Could not find a part of the path '{0}'.";
    public const string IO_PathTooLong‎ = "The specified file name or path is too long, or a component of the specified path is too long.";
    public const string IO_PathTooLong_Path‎ = "The path '{0}' is too long, or a component of the specified path is too long.";
    public const string IO_SeekBeforeBegin‎ = "An attempt was made to move the position before the beginning of the stream.";
    public const string IO_SharingViolation_File‎ = "The process cannot access the file '{0}' because it is being used by another process.";
    public const string IO_SharingViolation_NoFileName‎ = "The process cannot access the file because it is being used by another process.";
    public const string MailAddressInvalidFormat‎ = "The specified string is not in the form required for an e-mail address.";
    public const string MailHeaderFieldInvalidCharacter‎ = "An invalid character was found in the mail header: '{0}'.";
    public const string MailHeaderFieldMalformedHeader‎ = "The mail header is malformed.";
    public const string net_auth_message_not_encrypted‎ = "Protocol error: A received message contains a valid signature but it was not encrypted as required by the effective Protection Level.";
    public const string net_completed_result‎ = "This operation cannot be performed on a completed asynchronous result object.";
    public const string net_context_buffer_too_small‎ = "Insufficient buffer space. Required: {0} Actual: {1}.";
    public const string net_cookie_attribute‎ = "The '{0}'='{1}' part of the cookie is invalid.";
    public const string net_gssapi_operation_failed‎ = "GSSAPI operation failed with status: {0} (Minor status: {1}).";
    public const string net_gssapi_operation_failed_detailed‎ = "GSSAPI operation failed with error - {0} ({1}).";
    public const string net_http_argument_empty_string‎ = "The value cannot be null or empty.";
    public const string net_http_authconnectionfailure‎ = "Authentication failed because the connection could not be reused.";
    public const string net_http_buffer_insufficient_length‎ = "The buffer was not long enough.";
    public const string net_http_chunked_not_allowed_with_empty_content‎ = "'Transfer-Encoding: chunked' header can not be used when content object is not specified.";
    public const string net_http_client_absolute_baseaddress_required‎ = "The base address must be an absolute URI.";
    public const string net_http_client_execution_error‎ = "An error occurred while sending the request.";
    public const string net_http_client_http_baseaddress_required‎ = "Only 'http' and 'https' schemes are allowed.";
    public const string net_http_client_invalid_requesturi‎ = "An invalid request URI was provided. The request URI must either be an absolute URI or BaseAddress must be set.";
    public const string net_http_client_request_already_sent‎ = "The request message was already sent. Cannot send the same request message multiple times.";
    public const string net_http_content_buffersize_exceeded‎ = "Cannot write more bytes to the buffer than the configured maximum buffer size: {0}.";
    public const string net_http_content_buffersize_limit‎ = "Buffering more than {0} bytes is not supported.";
    public const string net_http_content_field_too_long‎ = "The field cannot be longer than {0} characters.";
    public const string net_http_content_invalid_charset‎ = "The character set provided in ContentType is invalid. Cannot read content as string using an invalid character set.";
    public const string net_http_content_no_concurrent_reads‎ = "The stream does not support concurrent read operations.";
    public const string net_http_content_no_task_returned‎ = "The async operation did not return a System.Threading.Tasks.Task object.";
    public const string net_http_content_readonly_stream‎ = "The stream does not support writing.";
    public const string net_http_content_stream_already_read‎ = "The stream was already consumed. It cannot be read again.";
    public const string net_http_content_stream_copy_error‎ = "Error while copying content to a stream.";
    public const string net_http_copyto_array_too_small‎ = "The number of elements is greater than the available space from arrayIndex to the end of the destination array.";
    public const string net_http_feature_requires_Windows10Version1607‎ = "Using this feature requires Windows 10 Version 1607.";
    public const string net_http_feature_UWPClientCertSupportRequiresCertInPersonalCertificateStore‎ = "Client certificate was not found in the personal (\\\"MY\\\") certificate store. In UWP, client certificates are only supported if they have been added to that certificate store.";
    public const string net_http_handler_norequest‎ = "A request message must be provided. It cannot be null.";
    public const string net_http_handler_noresponse‎ = "Handler did not return a response message.";
    public const string net_http_handler_not_assigned‎ = "The inner handler has not been assigned.";
    public const string net_http_headers_invalid_etag_name‎ = "The specified value is not a valid quoted string.";
    public const string net_http_headers_invalid_from_header‎ = "The specified value is not a valid 'From' header string.";
    public const string net_http_headers_invalid_header_name‎ = "The header name format is invalid.";
    public const string net_http_headers_invalid_host_header‎ = "The specified value is not a valid 'Host' header string.";
    public const string net_http_headers_invalid_range‎ = "Invalid range. At least one of the two parameters must not be null.";
    public const string net_http_headers_invalid_value‎ = "The format of value '{0}' is invalid.";
    public const string net_http_headers_not_allowed_header_name‎ = "Misused header name. Make sure request headers are used with HttpRequestMessage, response headers with HttpResponseMessage, and content headers with HttpContent objects.";
    public const string net_http_headers_not_found‎ = "The given header was not found.";
    public const string net_http_headers_no_newlines‎ = "New-line characters in header values must be followed by a white-space character.";
    public const string net_http_headers_single_value_header‎ = "Cannot add value because header '{0}' does not support multiple values.";
    public const string net_http_httpmethod_format_error‎ = "The format of the HTTP method is invalid.";
    public const string net_http_httpmethod_notsupported_error‎ = "The HTTP method '{0}' is not supported on this platform.";
    public const string net_http_invalid_cookiecontainer‎ = "When using CookieUsePolicy.UseSpecifiedCookieContainer, the CookieContainer property must not be null.";
    public const string net_http_invalid_enable_first‎ = "The {0} property must be set to '{1}' to use this property.";
    public const string net_http_invalid_proxy‎ = "When using WindowsProxyUsePolicy.UseCustomProxy, the Proxy property must not be null.";
    public const string net_http_invalid_proxyusepolicy‎ = "When using a non-null Proxy, the WindowsProxyUsePolicy property must be set to WindowsProxyUsePolicy.UseCustomProxy.";
    public const string net_http_invalid_proxy_scheme‎ = "Only the 'http' scheme is allowed for proxies.";
    public const string net_http_invalid_response‎ = "The server returned an invalid or unrecognized response.";
    public const string net_http_io_read‎ = "The read operation failed, see inner exception.";
    public const string net_http_io_read_incomplete‎ = "Unable to read data from the transport connection. The connection was closed before all data could be read. Expected {0} bytes, read {1} bytes.";
    public const string net_http_io_write‎ = "The write operation failed, see inner exception.";
    public const string net_http_libcurl_callback_notsupported_os‎ = "The handler does not support custom handling of certificates on this operating system. Consider using System.Net.Http.SocketsHttpHandler.";
    public const string net_http_libcurl_callback_notsupported_sslbackend‎ = "The handler does not support custom handling of certificates with this combination of libcurl ({0}) and its SSL backend (\"{1}\"). An SSL backend based on \"{2}\" is required. Consider using System.Net.Http.SocketsHttpHandler.";
    public const string net_http_libcurl_clientcerts_notsupported_os‎ = "The handler does not support client authentication certificates on this operating system. Consider using System.Net.Http.SocketsHttpHandler.";
    public const string net_http_libcurl_clientcerts_notsupported_sslbackend‎ = "The handler does not support client authentication certificates with this combination of libcurl ({0}) and its SSL backend (\"{1}\"). An SSL backend based on \"{2}\" is required. Consider using System.Net.Http.SocketsHttpHandler.";
    public const string net_http_libcurl_revocation_notsupported_sslbackend‎ = "The handler does not support changing revocation settings with this combination of libcurl ({0}) and its SSL backend (\"{1}\"). An SSL backend based on \"{2}\" is required. Consider using System.Net.Http.SocketsHttpHandler.";
    public const string net_http_log_headers_invalid_quality‎ = "The 'q' value is invalid: '{0}'.";
    public const string net_http_log_headers_no_newlines‎ = "Value for header '{0}' contains invalid new-line characters. Value: '{1}'.";
    public const string net_http_log_headers_wrong_email_format‎ = "Value '{0}' is not a valid email address. Error: {1}";
    public const string net_http_message_not_success_statuscode‎ = "Response status code does not indicate success: {0} ({1}).";
    public const string net_http_no_concurrent_io_allowed‎ = "The stream does not support concurrent I/O read or write operations.";
    public const string net_http_operation_started‎ = "This instance has already started one or more requests. Properties can only be modified before sending the first request.";
    public const string net_http_parser_invalid_base64_string‎ = "Value '{0}' is not a valid Base64 string. Error: {1}";
    public const string net_http_reasonphrase_format_error‎ = "The reason phrase must not contain new-line characters.";
    public const string net_http_request_invalid_char_encoding‎ = "Request headers must contain only ASCII characters.";
    public const string net_http_request_no_host‎ = "CONNECT request must contain Host header.";
    public const string net_http_response_headers_exceeded_length‎ = "The HTTP response headers length exceeded the set limit of {0} bytes.";
    public const string net_http_ssl_connection_failed‎ = "The SSL connection could not be established, see inner exception.";
    public const string net_http_unix_handler_disposed‎ = "The handler was disposed of while active operations were in progress.";
    public const string net_http_unix_https_support_unavailable_libcurl‎ = "The libcurl library in use ({0}) does not support HTTPS.";
    public const string net_http_unix_invalid_credential‎ = "The libcurl library in use ({0}) does not support different credentials for different authentication schemes.";
    public const string net_http_unsupported_chunking‎ = "HTTP 1.0 does not support chunking.";
    public const string net_http_unsupported_version‎ = "Request HttpVersion 0.X is not supported.  Use 1.0 or above.";
    public const string net_http_username_empty_string‎ = "The username for a credential object cannot be null or empty.";
    public const string net_http_value_must_be_greater_than‎ = "The specified value must be greater than {0}.";
    public const string net_http_value_not_supported‎ = "The value '{0}' is not supported for property '{1}'.";
    public const string net_http_winhttp_error‎ = "Error {0} calling {1}, '{2}'.";
    public const string net_invalid_enum‎ = "The specified value is not valid in the '{0}' enumeration.";
    public const string net_log_operation_failed_with_error‎ = "{0} failed with error {1}.";
    public const string net_MethodNotImplementedException‎ = "This method is not implemented by this class.";
    public const string net_nego_channel_binding_not_supported‎ = "No support for channel binding on operating systems other than Windows.";
    public const string net_nego_not_supported_empty_target_with_defaultcreds‎ = "Target name should be non empty if default credentials are passed.";
    public const string net_nego_protection_level_not_supported‎ = "Requested protection level is not supported with the gssapi implementation currently installed.";
    public const string net_nego_server_not_supported‎ = "Server implementation is not supported";
    public const string net_ntlm_not_possible_default_cred‎ = "NTLM authentication is not possible with default credentials on this platform.";
    public const string net_securitypackagesupport‎ = "The requested security package is not supported.";
    public const string net_securityprotocolnotsupported‎ = "The requested security protocol is not supported.";
    public const string net_ssl_app_protocols_invalid‎ = "The application protocol list is invalid.";
    public const string NotSupported_UnreadableStream‎ = "Stream does not support reading.";
    public const string NotSupported_UnwritableStream‎ = "Stream does not support writing.";
    public const string ObjectDisposed_StreamClosed‎ = "Can not access a closed Stream.";
    public const string SSPIInvalidHandleType‎ = "'{0}' is not a supported handle type.";
    public const string UnauthorizedAccess_IODenied_NoPathName‎ = "Access to the path is denied.";
    public const string UnauthorizedAccess_IODenied_Path‎ = "Access to the path '{0}' is denied.";

    public static string Format(string str, params object[] args) => string.Format(str, args);
}
