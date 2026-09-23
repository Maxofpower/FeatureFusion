"use client"

import * as React from "react"
import {
    BookOpen,
    Bot,
    Command,
    Frame,
    Layers,
    LayoutDashboard,
    LifeBuoy,
    Map,
    PieChart,
    Send,
    Settings2,
    SquareTerminal,
    Users,
    ShoppingCart,
    Tag,
    CirclePile
} from "lucide-react"


// import { NavUser } from "./nav-user"
import {
    Sidebar,
    SidebarContent,
    SidebarFooter,
    SidebarHeader,
    SidebarMenu,
    SidebarMenuButton,
    SidebarMenuItem,
} from "@/components/ui/sidebar"
import Link from "next/link"
import { NavItems } from "./nav-items"
import { APP_ROUTES } from "@/lib/constants/routes"

interface SidebarProps extends React.ComponentProps<typeof Sidebar> {
    pathname: string
}

const data = {
    items: [
        {
            name: "Home",
            url: APP_ROUTES.HOME,
            icon: LayoutDashboard,
        },
        {
            name: "Catalog",
            url: APP_ROUTES.CATALOG,
            icon: BookOpen,
        },
        {
            name: "Brands",
            url: APP_ROUTES.BRANDS,
            icon: Tag,
        },
        {
            name: "Categories",
            url: APP_ROUTES.CATEGORIES,
            icon: CirclePile,
        },
        {
            name: "Orders",
            url: "#",
            icon: ShoppingCart,
        },
        {
            name: "Customers",
            url: "#",
            icon: Users,
        },
    ],
}

export function AppSidebar({ pathname, ...props }: SidebarProps) {
    return (
        <Sidebar variant="inset" collapsible="icon" {...props}>
            <SidebarHeader>
                <SidebarMenu>
                    <SidebarMenuItem>
                        <SidebarMenuButton size="lg" className="transition-all">
                            <Link href={APP_ROUTES.HOME} className="flex items-center gap-2 w-full">
                                <div className="bg-sidebar-primary text-sidebar-primary-foreground flex aspect-square size-8 items-center justify-center rounded-lg">
                                    <Command className="size-4" />
                                </div>
                                <div className="grid flex-1 text-left text-sm leading-tight">
                                    <p className="truncate font-medium">Feature Fusion</p>
                                    <p className="truncate text-xs font-light">Lab</p>
                                </div>
                            </Link>
                        </SidebarMenuButton>
                    </SidebarMenuItem>
                </SidebarMenu>
            </SidebarHeader>
            <SidebarContent>
                <NavItems projects={data.items} pathname={pathname} />
            </SidebarContent>
            {/* <SidebarFooter>
                <NavUser />
            </SidebarFooter> */}
        </Sidebar>
    )
}
