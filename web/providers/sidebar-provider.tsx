'use client'

import { AppSidebar } from "@/components/sidebar/sidebar";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { usePathname } from "next/navigation";


export const SideBarProvider = ({ children }: { children: React.ReactNode }) => {
    const pathname = usePathname();


    return (
        <SidebarProvider>
            <AppSidebar pathname={pathname} />
            <SidebarInset>
                <header className="flex h-16 shrink-0 items-center gap-2">
                    <div className="flex items-center gap-2 px-4">
                        <SidebarTrigger className="-ml-1" />
                        <Separator
                            orientation="vertical"
                            className="mr-2 data-[orientation=vertical]:h-4 mt-1.5"
                        />
                        {/* <Breadcrumb>
                            <BreadcrumbList>
                                {breadcrumbs.map((item, index) => (
                                    <Fragment key={item.href}>
                                        {index > 0 && (
                                            <BreadcrumbSeparator className="hidden md:block" />
                                        )}
                                        <BreadcrumbItem>
                                            {item.isLast ? (
                                                <BreadcrumbPage>
                                                    {item.label}
                                                </BreadcrumbPage>
                                            ) : (
                                                <BreadcrumbLink href={item.href}>
                                                    {item.label}
                                                </BreadcrumbLink>
                                            )}
                                        </BreadcrumbItem>
                                    </Fragment>
                                ))}
                            </BreadcrumbList>
                        </Breadcrumb> */}
                    </div>
                </header>
                {children}
            </SidebarInset>
        </SidebarProvider>
    );
};