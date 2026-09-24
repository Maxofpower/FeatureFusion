'use client'


import { AppSidebar } from "@/components/sidebar/sidebar";
import {
    Breadcrumb,
    BreadcrumbItem,
    BreadcrumbLink,
    BreadcrumbList,
    BreadcrumbPage,
    BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Separator } from "@/components/ui/separator";
import {
    SidebarInset,
    SidebarProvider,
    SidebarTrigger,
} from "@/components/ui/sidebar";
import { usePathname } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { Fragment } from "react/jsx-runtime";
import { getProductName } from "./action";

export const SideBarProvider = ({
    children,
}: {
    children: React.ReactNode;
}) => {
    const pathname = usePathname();

    const [productName, setProductName] = useState<string>();

    useEffect(() => {
        const getName = async () => {
            const segments = pathname.split("/").filter(Boolean);

            if (
                segments[0] !== "catalog" ||
                segments[1] !== "product" ||
                !segments[2]
            ) {
                setProductName(undefined);
                return;
            }

            const slug = segments[2];

            const name = await getProductName(
                `slug=${encodeURIComponent(slug)}`
            );

            setProductName(name);
        };

        getName();
    }, [pathname]);

    const breadcrumbs = useMemo(() => {
        const segments = pathname.split("/").filter(Boolean);

        return segments.map((segment, index) => {
            const href = "/" + segments.slice(0, index + 1).join("/");

            const isProductSlug =
                segments[0] === "catalog" &&
                segments[1] === "product" &&
                index === 2;

            const label =
                isProductSlug && productName
                    ? productName
                    : segment.charAt(0).toUpperCase() +
                      segment.slice(1).replace(/-/g, " ");

            return {
                href,
                label,
                isLast: index === segments.length - 1,
            };
        });
    }, [pathname, productName]);

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

                        <Breadcrumb>
                            <BreadcrumbList>
                                {breadcrumbs.map((item, index) => (
                                    <Fragment key={item.href}>
                                        {index > 0 && (
                                            <BreadcrumbSeparator />
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
                        </Breadcrumb>
                    </div>
                </header>

                {children}
            </SidebarInset>
        </SidebarProvider>
    );
};