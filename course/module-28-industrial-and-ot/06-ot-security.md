# OT security

> Why the line is on its own network, what the Purdue model is, why you may not patch the HMI, and
> how data actually gets out — which is the conversation you will be having in your first month.

[Module 24](../module-24-security/) is security for an API you own, running on infrastructure you
control, patched on a schedule you set. Almost none of those assumptions survive contact with a
factory floor, and the gap between them is where a well-meaning developer causes a genuine incident.

---

## 1. The inversion: availability first

IT security ranks confidentiality, integrity, availability — roughly in that order. OT inverts it,
and the inversion is not laziness:

| | IT | OT |
|---|---|---|
| Worst outcome | data leaks | **the line stops, or somebody is hurt** |
| Patching | promptly, automatically | only in a planned window, only after the vendor validates it |
| Downtime for security | acceptable | measured in thousands of euro an hour |
| Lifetime of a system | 3–5 years | **15–25 years** |
| Reboot to apply a fix | fine | requires a production plan and possibly a re-qualification |

A machine that stops unexpectedly can destroy work in progress, damage tooling, and — the reason
this is not merely commercial — put a person in a bad position. Safety systems are engineered,
certified and signed off by somebody whose name is on the document, and software that interferes
with them is a different category of mistake from a data breach.

**So when the OT team says no, they are not being obstructive.** They are measured on uptime and
safety, and you have just proposed changing something that currently works. The way through is to
arrive with the failure mode already answered: what happens when your thing is down, what happens
when the link drops, what it touches and — most importantly — what it cannot touch.

---

## 2. The Purdue model, which is the diagram in the room

The Purdue Enterprise Reference Architecture is the network layering everyone draws, and it maps
onto the ISA-95 levels you already know:

```
   Level 5   enterprise network — email, the internet
   Level 4   business / site IT — ERP, file servers
   ──────────────────────────────────────────────────────  ← the firewall pair
   Level 3.5 the DMZ            historian replica, gateway, jump host, patch server
   ──────────────────────────────────────────────────────  ← and the second firewall
   Level 3   site operations    MES, WCS, the historian
   Level 2   supervisory        SCADA, HMI
   Level 1   control            PLCs
   Level 0   process            sensors and actuators
```

The important part is **Level 3.5, the DMZ**, because it is where you will actually be asked to put
something. The rule it enforces: *nothing crosses from enterprise to control in one hop.* A system
in the DMZ can be reached from above and can reach downward; nothing reaches straight through. When
somebody says "put it in the DMZ", that is what they mean, and answering "which side of the second
firewall?" marks you as somebody who has seen the diagram before.

Worth knowing: the model is genuinely showing its age — it assumes a clean hierarchy that cloud
connectivity, remote vendor access and wireless devices have all punched holes in — and there is a
long-running industry argument about what replaces it. You do not need a position in that argument.
You need to recognise the diagram and know which layer you are being put in.

---

## 3. The constraints that will surprise you

**You may not patch the HMI.** A Windows box from 2009, running a vendor's SCADA package, whose
support contract explicitly voids if the OS is updated. It is not connected to Windows Update
because it is not connected to anything that reaches the internet. This is normal and it is not
going to change on your account. You compensate around it — network segmentation, no inbound
paths, monitoring — rather than fixing it, and proposing "we should just upgrade it" without
knowing about the warranty is how you spend your credibility in week two.

**Passwords are shared and written down, and the reason is not stupidity.** An operator wearing
gloves at 03:00 with a line stopped is not going to type a 16-character password, and a system that
locks them out during a fault is a system that gets bypassed permanently. Individual accounts
belong where individual accountability is required — signing off a batch, authorising a deviation,
acknowledging an alarm — and those are exactly the places to spend the effort. Fighting for
per-operator login on a start button is losing the argument you should be winning elsewhere.

**Default credentials are everywhere.** PLCs, HMI panels, network switches, VFDs, the camera above
the line. Half of them cannot have their credentials changed without vendor involvement. This is
what segmentation is really compensating for, and it is why "we'll just put the machines on the
office VLAN" is a genuinely dangerous suggestion rather than a lazy one.

**Remote access for vendors is the real attack surface.** The machine builder needs to dial in when
something breaks — usually urgently, at a bad hour, from another country. The right shape is a
jump host in the DMZ, access granted per incident rather than standing, multi-factor at the
boundary, and the session recorded. The shape you will often find instead is a modem, a TeamViewer
licence, or a VPN account created in 2016 for somebody who left in 2018.

**Protocols have no authentication at all.** Modbus has none: anybody who can reach the port can
write a register. Classic OPC and most fieldbus protocols were designed for a physically isolated
network, and their security model *is* that isolation. This is the single most important thing to
understand about why the network layout matters so much — the protocols are not going to defend
themselves. OPC UA is the exception, which is why its certificate handling
([chapter 2](02-protocols-on-the-wire.md)) is worth taking seriously rather than switching off.

---

## 4. How data actually gets out

You will be asked to get production data to a dashboard, a cloud service, or head office. The
answer is not "open a firewall rule".

**Push outward, never pull inward.** A component inside OT initiates the connection and sends data
out. Nothing outside gets to open a connection into the control network. This one rule resolves
most of the argument, because it means a compromise of the cloud side cannot reach a PLC.

**Store and forward, always.** The link will drop. If your gateway loses data when it does, you
have built something that fails exactly when the network is having a bad day — which is when people
are already looking. Buffer locally, forward on reconnect, and make the receiving end idempotent
because replay is now guaranteed. [Module 25](../module-25-distributed-systems/) is the whole of
this problem and none of it is new.

**A historian is usually already there.** Most plants have one — a time-series database (PI, Ignition,
Aveva, or an InfluxDB somebody set up) already collecting tags. Reading from the historian instead
of from the PLCs is very often the right answer: it is designed for it, it does not add load to the
control layer, and the OT team already trusts it. **Ask about the historian before you propose
anything**, because "we already collect that" is a common and welcome answer.

**A data diode is the strong version.** Hardware that physically only transmits in one direction —
no acknowledgement is even possible. You will meet it in energy, water and defence rather than in a
ceramics plant, but knowing the term signals that you understand the strength of the requirement.

**And the honest sentence for the interview:** *"Getting data out is usually achievable — outbound
only, through a gateway in the DMZ, with store-and-forward for when the link drops. What I would
want to agree first is what happens when it is down, and confirmation that nothing new can reach
the control network."* That is a proposal with the OT team's objection already answered, and it is
a different thing from an architecture diagram with an arrow pointing at a cloud.

---

## 5. What this means for your code

- **Fail safe, and be specific about what safe means.** If your supervisor dies, the line must keep
  running or stop cleanly — never continue in a state your software was meant to be arbitrating.
  Decide which, deliberately, and write it down.
- **Never assume the machine is trustworthy.** A tag value can be nonsense because a sensor failed,
  and a temperature of 3,000 °C should be rejected at your boundary rather than stored and averaged.
  Range-check at the edge; it is input validation, and the input is a device.
- **Log every command you send.** What, when, who asked for it, and what came back. When the
  question is "did the software do this", the log is the answer, and there will be a day when the
  question is asked seriously.
- **Read-only until proven otherwise.** Collecting data is a much easier conversation than writing
  to a PLC. Do not ask for write access you do not yet need — you will want the goodwill later.
- **Secrets are not in the config file on the HMI.** [Module 13](../module-13-deployment/) applies
  here as much as anywhere, and the machine sitting in a public area of the factory makes it
  matter more, not less.

---

## Golden rules

1. **OT ranks availability first.** Confidentiality, integrity, availability inverts — and a stopped
   line is a worse outcome than a leaked temperature reading.
2. **"The OT team will never allow that" is a statement about incentives**, not obstruction. Arrive
   with the failure mode already answered.
3. **Nothing crosses from enterprise to control in one hop.** That is what the DMZ at Level 3.5 is
   for, and knowing the layer marks you as someone who has seen the diagram.
4. **You may not patch the HMI**, and the vendor warranty is the reason. Compensate around it.
5. **Fieldbus protocols have no authentication.** Their security model is physical isolation, which
   is why the network layout carries the entire load.
6. **Shared operator credentials are a design response to gloves at 03:00.** Spend individual
   accountability where it is required — batch sign-off, deviations, alarm acknowledgement.
7. **Vendor remote access is the real attack surface.** Per-incident, through a jump host,
   recorded — not a standing VPN account from 2016.
8. **Data leaves outward only, with store-and-forward.** Nothing outside opens a connection inward.
9. **Ask whether there is already a historian** before proposing to collect anything.
10. **Range-check values at your boundary.** A device is an untrusted input like any other.
11. **Log every command you send.** One day somebody will ask, seriously, whether the software did
    it.

---

[← OEE and traceability](05-oee-and-traceability.md) · [Module 28](README.md) · [The job itself →](07-the-job.md)
