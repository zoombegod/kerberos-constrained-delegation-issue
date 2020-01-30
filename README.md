# Kerberos constrained delegation triple hop issue with co-located components

This repository contains all necessary code and instructions to reproduce an issue with Kerberos in Windows environments where constrained delegation fails if the middle hop in a triple hop scenario happens on a local server.

## Preconditions/environment

- Active Directory for a single domain
- 4 Windows Server 2016 with IIS 10 (the instances will be called `W`, `X`, `Y`, and `Z`), all joined to the same domain
  - note that the issue can also be reproduced on Windows Server 2012 R2 with IIS 8.5
- 4 domain user accounts (called `KerbCD_1`, `KerbCD_2`, `KerbCD_3`, and `KerbCD_4`); the first three will be used to run application pools in IIS
- (optional but recommended) have wireshark (version 3.2.1) installed on servers `X` and `Y` to observer Kerberos network communication

## Detailed problem description

The image below shows the situation.

![Kerberos Constrained Delegation triple hop issue](/image.png?raw=true)

A client (in this scenario represented by server `W`) wants to access the resource `KerbCD_E` on server `Z`. `KerbCD_E` requires Kerberos authentication, therefore the identity of the user must be delegated from `W` to `Z`. This delegation fails if two components in the delegation chain reside on the same server (whether the same or a different app pool user account is used seems to make no difference). Based on the numbered arrows above, in the following scenarios the call to `KerbCD_E` fails:

- `1 -> 3 -> 4`
- `2 -> 7 -> 8`

If all components in the chain are on separate servers authentication and delegation to `KerbCD_E` succeeds.

- `1 -> 5 -> 9`
- `1 -> 6 -> 8`

In the scenarios where the call fails, we can observer via Wireshark that an incorrect service ticket is used. For example, in the scenario `1 -> 3 -> 4` `KerbCD_A` correctly delegates the user to `KerbCD_B`, but when `KerbCD_B` wants to call `KerbCD_E` it performs protocol transition, which leads to a ticket that is (correctly) not accepted by `KerbCD_E`.

And additional issue is that a chain that works stops working after a non-working chain is used (likely due to credential caching), e.g. when using `1 -> 6 -> 8` (success), then `2 -> 7 -> 8` (success), and finally `1 -> 6 -> 8` the last call will now also fail.

## Environment setup steps

1. on servers `W`, `X`, `Y`, and `Z` create entries in the `hosts` file for all other servers as follows (alternatively create proper DNS entries, but entries in `hosts` files are enough to show the issue and are simpler to set up); replace the IPs according to your environment
    ```
    a.kerb-cd.test 10.0.0.X
    b.kerb-cd.test 10.0.0.X
    c.kerb-cd.test 10.0.0.Y
    d.kerb-cd.test 10.0.0.Y
    e.kerb-cd.test 10.0.0.Z
    ```
1. on server `W` install IE11 and Edge (this server simulates the client device)
1. on server `W` in the internet options add `http://*.kerb-cd.test` as a trusted website in the local intranet zone
1. on servers `X`, `Y`, and `Z` in IIS:
    - remove the default website and app pool
    - on the server level set `useAppPoolCredentials` to `true` in `system.webServer/security/authentication/windowsAuthentication` via the configuration manager
    - on the server level unlock the `anonymousAuthentication` and `windowsAuthentication` sections of `system.webServer/security/authentication` via the configuration manager
1. on server `X`
    - create the directories `C:\KerbCD\A`, `C:\KerbCD\B`, and `C:\KerbCD\logs`
    - in IIS create two app pools `KerbCD_A` and `KerbCD_B` (`.NET CLR Version 4.0.30319`, mode `Integrated`)
        - both app pools use the `KerbCD_1` domain user
    - in IIS create two websites `KerbCD_A` and `KerbCD_B` using the app pool of the same name each
        - map them to their corresponding folders on disk
        - use `a.kerb-cd.test` resp. `b.kerb-cd.test` as a domain name (i.e. ensure each website has one HTTP binding for port 80 for the corresponding domain name)
1. on server `Y`
    - create the directories `C:\KerbCD\C`, `C:\KerbCD\D`, and `C:\KerbCD\logs`
    - in IIS create two app pools `KerbCD_C` and `KerbCD_D` (`.NET CLR Version 4.0.30319`, mode `Integrated`)
        - `KerbCD_C` uses the `KerbCD_1` domain user
        - `KerbCD_D` uses the `KerbCD_2` domain user
    - in IIS create two websites `KerbCD_C` and `KerbCD_D` using the app pool of the same name each
        - map them to their corresponding folders on disk
        - use `c.kerb-cd.test` resp. `d.kerb-cd.test` as a domain name (i.e. ensure each website has one HTTP binding for port 80 for the corresponding domain name)
1. on server `Z`
    - create the directories `C:\KerbCD\E` and `C:\KerbCD\logs`
    - in IIS create an app pool `KerbCD_E` (`.NET CLR Version 4.0.30319`, mode `Integrated`)
        - `KerbCD_E` uses the `KerbCD_3` domain user
    - in IIS create a website `KerbCD_E`
        - map it to its corresponding folder on disk
        - use `e.kerb-cd.test` as a domain name (i.e. ensure the website has one HTTP binding for port 80 for the corresponding domain name)
1. map the SPNs for the domain acounts
    ```
    setspn -A HTTP/a.kerb-cd.test AD\KerbCD_1
    setspn -A HTTP/b.kerb-cd.test AD\KerbCD_1
    setspn -A HTTP/c.kerb-cd.test AD\KerbCD_1
    setspn -A HTTP/d.kerb-cd.test AD\KerbCD_2
    setspn -A HTTP/e.kerb-cd.test AD\KerbCD_3
    ```
1. configure the domain accounts for constrained delegation
    - for `KerbCD_1` and `KerbCD_2` choose `Trust this user for delegation to specified services only` with `Use Kerberos only`
    - for `KerbCD_1` allow delegation to `HTTP/b.kerb-cd.test`, `HTTP/c.kerb-cd.test`, `HTTP/d.kerb-cd.test`, and `HTTP/e.kerb-cd.test`
    - for `KerbCD_2` allow delegation to `HTTP/e.kerb-cd.test`

## Reproduction steps

1. Build and publish the `KerbCDRepro` project from this repository
    - this project is set up to allow recursively call itself via HTTP by providing the targets as a query parameter
1. copy the content of the publish folder (e.g. `KerbCDRepro/bin/Release/Publish`) to the `C:\KerbCD\A` etc. folders on the servers `X`, `Y`, and `Z`
1. ensure all websites on servers `X`, `Y`, and `Z` are running
1. (optional) start a Wireshark trace on servers `X` and `Y` with filter `kerberos or http`
1. log onto server `W` as user `KerbCD_4`
1. in IE11 / Edge on server `W` browse to `http://a.kerb-cd.test/kerberosTest?targets=d,e`
    - this call will execute scenario `1 -> 6 -> 8` from the image above
    - this should show `"A -> D -> E: AD\KerbCD_4"`
    - (optional) in wireshark you will see the correct `TGS-REQ` and `TGS-REP` pairs
1. browse to `http://a.kerb-cd.test/kerberosTest?targets=b,e`
    - this call will execute scenario `1 -> 3 -> 4` from the image above
    - this should show `"A -> D -> E: failed"`
    - (optional) in wireshark you will see that for the last `TGS-REQ` (i.e. from `KerbCD_B` to `KerbCD_E`) an error is returned from the KDC (with error code 13, i.e. `ERR_BADOPTION`); you will also see that `KerbCD_B` attempts an NTLM fallback, which fails as expected
