﻿//-----------------------------------------------------------------------------
// Filename: SIPDialPlanJSON.cs
//
// Description: A translation class to allow a SIP account to be serialised to and from JSON. The 
// Entity Framework derived SIPAccount class cannot be used to a shortcoming in the serialisation
// mechanism to do with IsReference classes. This will probably be fixed in the future making this
// class redundant.
// 
// History:
// 27 Apr 2013	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2013 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty. Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Entities
{
    [DataContractAttribute]
    public class SIPDialPlanJSON
    {
        [DataMember] public string ID { get; set; }
        [DataMember] public string DialPlanName { get; set; }
        [DataMember] public string TraceEmailAddress { get; set; }
        [DataMember] public string DialPlanScript { get; set; }
        [DataMember] public string ScriptTypeDescription { get; set; }
        [DataMember] public bool AcceptNonInvite { get; set; }

        public SIPDialPlanJSON()
        { }

        public SIPDialPlan ToSIPDialPlan()
        {
            var entitySIPDialPlan = new SIPDialPlan()
            {
                Owner = String.Empty,
                ID = ID,
                DialPlanName = DialPlanName,
                TraceEmailAddress = TraceEmailAddress,
                DialPlanScript = DialPlanScript,
                ScriptTypeDescription = ScriptTypeDescription,
                AcceptNonInvite = AcceptNonInvite
            };

            return entitySIPDialPlan;
        }
    }
}
