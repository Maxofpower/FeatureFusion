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
import { Skeleton } from "@/components/ui/skeleton";
import {
    SidebarInset,
    SidebarProvider,
    SidebarTrigger,
} from "@/components/ui/sidebar";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { Fragment } from "react/jsx-runtime";
import { getOrderNumber, getProductName } from "./action";

type DynamicType = "product" | "order";

const getDynamicType = (
    segments: string[],
    index: number
): DynamicType | null => {
    if (segments[0] === "catalog" && segments[1] === "products" && index === 2) {
        return "product";
    }
    if (segments[0] === "orders" && index === 1) {
        return "order";
    }
    return null;
};

const makeKey = (type: DynamicType, value: string) => `${type}:${value}`;

const formatSegment = (segment: string) =>
    segment.charAt(0).toUpperCase() + segment.slice(1).replace(/-/g, " ");

export const SideBarProvider = ({
    children,
}: {
    children: React.ReactNode;
}) => {
    const pathname = usePathname();

    const [names, setNames] = useState<Map<string, string | null>>(new Map());


    const inFlight = useRef<Set<string>>(new Set());

    useEffect(() => {
        const segments = pathname.split("/").filter(Boolean);

        segments.forEach((segment, index) => {
            const type = getDynamicType(segments, index);
            if (!type) return;

            const key = makeKey(type, segment);
            if (names.has(key) || inFlight.current.has(key)) return;

            inFlight.current.add(key);

            const load = async () => {
                let name: string | undefined;

                try {
                    name =
                        type === "product"
                            ? await getProductName(segment)
                            : await getOrderNumber(segment);
                } catch (error) {
                    console.error("Failed to fetch breadcrumb label:", error);
                }

                inFlight.current.delete(key);
                setNames((prev) => new Map(prev).set(key, name ?? null));
            };

            load();
        });

    }, [pathname]);

    const generateBreadcrumbs = () => {
        const segments = pathname.split("/").filter(Boolean);

        return segments.map((segment, index) => {
            const href = "/" + segments.slice(0, index + 1).join("/");
            const isLast = index === segments.length - 1;

            let label: React.ReactNode = formatSegment(segment);

            const type = getDynamicType(segments, index);

            if (type) {
                const key = makeKey(type, segment);

                if (!names.has(key)) {

                    label = <Skeleton className="h-4 w-24 inline-block" />;
                } else {

                    label = names.get(key) ?? label;
                }
            }

            return { label, href, isLast };
        });
    };

    const breadcrumbs = generateBreadcrumbs();

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
                                        {index > 0 && <BreadcrumbSeparator />}

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