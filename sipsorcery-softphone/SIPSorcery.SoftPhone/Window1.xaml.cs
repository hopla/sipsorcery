﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Heijden.DNS;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public partial class Window1 : Window
    {
        private const int DNS_LOOKUP_TIMEOUT = 10000;

        private delegate void SetVisibilityDelegate(UIElement element, Visibility visibility);
        private delegate void AppendTraceMessageDelegate(string message);
        private delegate void ClearParagraphDelegate(Paragraph paragraph);

        private ILog logger = SIPSoftPhoneState.logger;

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;

        private FlowDocument m_traceDocument = new FlowDocument();
        private Paragraph m_traceParagraph = new Paragraph();

        private FlowDocument m_sipTransactionTraceDocument = new FlowDocument();
        private Paragraph m_sipTransactionTraceParagraph = new Paragraph();

        private FlowDocument m_sipTransportTraceDocument = new FlowDocument();
        private Paragraph m_sipTransportTraceParagraph = new Paragraph();

        private SIPTransport m_sipTransport;
        private SIPClientUserAgent m_uac;
        private SIPServerUserAgent m_uas;
        private STUNClient m_stunClient;
        private ManualResetEvent m_dnsLookupComplete = new ManualResetEvent(false);

        public Window1()
        {
            InitializeComponent();

            m_sipTraceGrid.Visibility = Visibility.Collapsed;
            m_uasGrid.Visibility = Visibility.Collapsed;

            m_traceDocument.Blocks.Add(m_traceParagraph);
            m_callLogRichTextBox.Document = m_traceDocument;

            m_sipTransactionTraceDocument.Blocks.Add(m_sipTransactionTraceParagraph);
            m_sipTraceRichTextBox.Document = m_sipTransactionTraceDocument;

            m_sipTransportTraceDocument.Blocks.Add(m_sipTransportTraceParagraph);

            m_cancelButton.Visibility = Visibility.Collapsed;
            m_byeButton.Visibility = Visibility.Collapsed;

            m_stunClient = new STUNClient(IPAddress.Parse("10.0.0.100"), new IPEndPoint(IPAddress.Parse("75.101.138.128"), 3478));

            ThreadPool.QueueUserWorkItem(InitialiseSIP);
            ThreadPool.QueueUserWorkItem(m_stunClient.GetPublicIPAddress);
            SIPDNSManager.SIPMonitorLogEvent += (e) => { AppendTraceMessage(e.Message + "\n"); };
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (m_sipTransport != null)
            {
                m_sipTransport.Shutdown();
            }

            DNSManager.Stop();
        }

        private void InitialiseSIP(object state)
        {
            // Configure the SIP transport layer.
            m_sipTransport = new SIPTransport(SIPDNSManager.Resolve, new SIPTransactionEngine(), false, false);
            List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipSocketsNode);
            m_sipTransport.AddSIPChannel(sipChannels);

            m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

            m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { AppendSIPTransportTraceMessage("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { AppendSIPTransportTraceMessage("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { AppendSIPTransportTraceMessage("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
            m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { AppendSIPTransportTraceMessage("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
        }

        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                string dialogueId = SIPDialogue.GetDialogueId(sipRequest.Header);

                if (m_uac != null && m_uac.SIPDialogue != null && dialogueId == m_uac.SIPDialogue.DialogueId)
                {
                    // Call has been hungup by remote end.
                    AppendTraceMessage("Call hungup by server: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".\n");
                    AppendSIPTraceMessage("Request Received " + localSIPEndPoint + "<-" + remoteEndPoint + "\n" + sipRequest.ToString());
                    m_uac.Hungup(localSIPEndPoint, remoteEndPoint, sipRequest);
                    ResetToCallStartState();
                }
                else if (m_uas != null && m_uas.SIPDialogue != null && dialogueId == m_uas.SIPDialogue.DialogueId)
                {
                    // Call has been hungup by remote end.
                    AppendTraceMessage("Call hungup by client: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".\n");
                    AppendSIPTraceMessage("Request Received " + localSIPEndPoint + "<-" + remoteEndPoint + "\n" + sipRequest.ToString());
                    m_uas.Hungup(localSIPEndPoint, remoteEndPoint, sipRequest);
                    ResetToCallStartState();
                }
                else
                {
                    AppendTraceMessage("Unmatched BYE request received for " + sipRequest.URI.ToString() + ".\n");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                ResetToCallStartState();
                ClearTraces();

                AppendTraceMessage("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".\n");
                SetVisibility(m_uacGrid, Visibility.Collapsed);
                SetVisibility(m_uasGrid, Visibility.Visible);

                m_uas = new SIPServerUserAgent(m_sipTransport, null, null, LogTraceMessage);
                m_uas.NewCall += UASNewCall;
                m_uas.CallCancelled += new SIPIncomingCallCancelledDelegate(UASCallCancelled);
                m_uas.InviteRequestReceived(sipRequest, localSIPEndPoint, remoteEndPoint);
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    AppendTraceMessage("Matching CANCEL request received " + sipRequest.URI.ToString() + ".\n");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                    cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    AppendTraceMessage("No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".\n");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else
            {
                AppendTraceMessage("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.\n");
                SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                m_sipTransport.SendResponse(notAllowedResponse);
            }
        }

        private void UASCallCancelled(SIPServerUserAgent uas)
        {
            AppendTraceMessage("Incoming call cancelled for: " + uas.SIPTransaction.TransactionRequest.URI.ToString() + "\n");
            ResetToCallStartState();
        }

        private void UASNewCall(SIPServerUserAgent uas)
        {
            AppendTraceMessage("Incoming call accepted for: " + uas.SIPTransaction.TransactionRequest.URI.ToString() + "\n");

            m_uas.SetRinging();
        }

        private void ResetToCallStartState()
        {
            SetVisibility(m_callButton, Visibility.Visible);
            SetVisibility(m_cancelButton, Visibility.Collapsed);
            SetVisibility(m_byeButton, Visibility.Collapsed);
            SetVisibility(m_answerButton, Visibility.Visible);
            SetVisibility(m_rejectButton, Visibility.Visible);
            SetVisibility(m_redirectButton, Visibility.Visible);
            SetVisibility(m_hangupButton, Visibility.Visible);
            SetVisibility(m_uacGrid, Visibility.Visible);
            SetVisibility(m_uasGrid, Visibility.Collapsed);
        }

        private void ClearTraces()
        {
            ClearParagraph(m_traceParagraph);
            ClearParagraph(m_sipTransactionTraceParagraph);
        }

        private void ShowTraceButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_sipTraceGrid.Visibility == Visibility.Collapsed)
            {
                m_sipTraceGrid.Visibility = Visibility.Visible;
                m_showTraceButton.Content = "<";
            }
            else
            {
                m_sipTraceGrid.Visibility = Visibility.Collapsed;
                m_showTraceButton.Content = ">";
            }
        }

        private void CallButton_Click(object sender, RoutedEventArgs e)
        {
            ClearTraces();
            AppendTraceMessage("Call starting: " + m_uriEntryTextBox.Text + "\n");

            m_callButton.Visibility = Visibility.Collapsed;
            m_cancelButton.Visibility = Visibility.Visible;
            m_byeButton.Visibility = Visibility.Collapsed;

            ThreadPool.QueueUserWorkItem(new WaitCallback(PlaceCall), m_uriEntryTextBox.Text);
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_uac != null)
            {
                AppendTraceMessage("Cancelling: " + m_uriEntryTextBox.Text + "\n");
                m_uac.Cancel();
            }
        }

        private void ByeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uac.Hangup();
            ResetToCallStartState();
        }

        private void AnswerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Answer(null, null, null, null);
            SetVisibility(m_answerButton, Visibility.Collapsed);
            SetVisibility(m_rejectButton, Visibility.Collapsed);
        }

        private void RejectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
            ResetToCallStartState();
        }

        private void RedirectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURI(m_redirectURIEntryTextBox.Text));
            ResetToCallStartState();
        }

        private void HangupButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ResetToCallStartState();
        }

        private void ClearTraceButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ClearParagraph(m_sipTransactionTraceParagraph);
            ClearParagraph(m_sipTransportTraceParagraph);
        }

        private void TransactionRadioButton_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_sipTraceRichTextBox != null)
            {
                m_sipTraceRichTextBox.Document = m_sipTransactionTraceDocument;
            }
        }

        private void AllRadioButton_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_sipTraceRichTextBox != null)
            {
                m_sipTraceRichTextBox.Document = m_sipTransportTraceDocument;
            }
        }

        private void DNSLookup(object state)
        {
            string callURIStr = (string)state;
            SIPEndPoint result = SIPDNSManager.Resolve(callURIStr);

            if (result != null) {
                AppendTraceMessage("DNS result for " + callURIStr + " is " + result.ToString() + ".\n");
            }
            else {
                AppendTraceMessage("DNS lookup for " + callURIStr + " failed.\n");
            }

            /*if (callURI != null)
            {
                VerboseDNSLookup(callURI.Host, DNSQType.NAPTR);
                VerboseDNSLookup("_sip._tls." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup("_sip._tcp." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup("_sip._udp." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup(callURI.Host, DNSQType.A);
            }
            else
            {
                AppendTraceMessage("SIP URI could not be parsed from " + callURIStr + ".\n");
            }*/

            m_dnsLookupComplete.Set();
        }

        private void VerboseDNSLookup(string host, DNSQType queryType) {
            AppendTraceMessage("Looking up " + host + " query type " + queryType.ToString() + ".\n");

            try {
                //DNSResponse dnsResponse = DNSManager.Lookup(host, queryType, 5, new List<IPEndPoint>{new IPEndPoint(IPAddress.Parse("128.59.16.20"), 53)}, false, false);
                DNSResponse dnsResponse = DNSManager.Lookup(host, queryType, 5, null, false, false);
                if (dnsResponse.Error == null) {
                    List<AnswerRR> records = dnsResponse.Answers;
                    //if (records.Count > 0)
                    if (dnsResponse.RecordNAPTR.Length > 0) {
                        AppendTraceMessage("Results for " + host + " query type " + queryType.ToString() + ".\n");
                        //foreach (AnswerRR record in records)
                        foreach (RecordNAPTR record in dnsResponse.RecordNAPTR) {
                            AppendTraceMessage(record.ToString() + "\n");
                        }
                    }
                    else {
                        AppendTraceMessage("Empty result returned for " + host + " query type " + queryType.ToString() + ".\n");
                    }
                }
                else {
                    AppendTraceMessage("DNS lookup error for " + host + " query type " + queryType.ToString() + ", " + dnsResponse.Error + ".\n");
                }
            }
            catch (ApplicationException appExcp) {
                AppendTraceMessage(appExcp.Message);
            }
        }

        private void PlaceCall(object state)
        {
            string callURI = (string)state;

            /*m_dnsLookupComplete.Reset();
            ThreadPool.QueueUserWorkItem(new WaitCallback(DNSLookup), callURI);
            if (!m_dnsLookupComplete.WaitOne(DNS_LOOKUP_TIMEOUT)) {
                AppendTraceMessage("DNS lookup for " + callURI + " timed out.\n");
                ResetToCallStartState();
            }
            else {*/
                AppendTraceMessage("Starting call to " + callURI + ".\n");
                m_uac = new SIPClientUserAgent(m_sipTransport, null, null, null, LogTraceMessage);
                m_uac.CallTrying += CallTrying;
                m_uac.CallRinging += CallRinging;
                m_uac.CallAnswered += CallAnswered;
                m_uac.CallFailed += CallFailed;
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor("anonymous", null, callURI, null, null, null, null, null, SIPCallDirection.Out, null, null);
                m_uac.Call(callDescriptor);
            //}
        }

        private void CallFailed(SIPClientUserAgent uac, string errorMessage)
        {
            AppendTraceMessage("Call failed: " + errorMessage + "\n");
            ResetToCallStartState();
        }

        private void CallAnswered(SIPClientUserAgent uac, SIPResponse sipResponse)
        {
            AppendTraceMessage("Call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".\n");

            if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
            {
                SetVisibility(m_callButton, Visibility.Collapsed);
                SetVisibility(m_cancelButton, Visibility.Collapsed);
                SetVisibility(m_byeButton, Visibility.Visible);
            }
            else
            {
                ResetToCallStartState();
            }
        }

        private void CallRinging(SIPClientUserAgent uac, SIPResponse sipResponse)
        {
            AppendTraceMessage("Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".\n");
        }

        private void CallTrying(SIPClientUserAgent uac, SIPResponse sipResponse)
        {
            AppendTraceMessage("Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".\n");
        }

        private void LogTraceMessage(SIPMonitorEvent monitorEvent)
        {
            if (monitorEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                monitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction)
            {
                AppendTraceMessage(monitorEvent.Message + "\n");
            }
            else
            {
                AppendSIPTraceMessage(monitorEvent.Message);
            }
        }

        private void AppendTraceMessage(string message)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new AppendTraceMessageDelegate(AppendTraceMessage), message);
                return;
            }

            m_traceParagraph.Inlines.Add(new Run(message));
        }

        private void AppendSIPTraceMessage(string message)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new AppendTraceMessageDelegate(AppendSIPTraceMessage), message);
                return;
            }

            m_sipTransactionTraceParagraph.Inlines.Add(new Run(message));
        }

        private void AppendSIPTransportTraceMessage(string message)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new AppendTraceMessageDelegate(AppendSIPTransportTraceMessage), message);
                return;
            }

            m_sipTransportTraceParagraph.Inlines.Add(new Run(message));
        }

        private void SetVisibility(UIElement element, Visibility visibility)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new SetVisibilityDelegate(SetVisibility), element, visibility);
                return;
            }

            element.Visibility = visibility;
        }

        private void ClearParagraph(Paragraph paragraph)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new ClearParagraphDelegate(ClearParagraph), paragraph);
                return;
            }

            paragraph.Inlines.Clear();
        }
    }
}